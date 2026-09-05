using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchNotes.Data;

namespace PatchNotes.Sync.Core;

/// <summary>
/// Orchestrates sync and summary generation as a producer-consumer pipeline.
/// The producer syncs packages from GitHub and writes package IDs to a channel.
/// The consumer reads package IDs and generates AI summaries concurrently.
/// </summary>
public class SyncPipeline
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncPipeline> _logger;

    public SyncPipeline(IServiceScopeFactory scopeFactory, ILogger<SyncPipeline> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs the sync pipeline: sync packages (producer) and generate summaries (consumer) concurrently.
    /// </summary>
    public async Task<PipelineResult> RunAsync(CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(5)
        {
            SingleWriter = true,
            SingleReader = true,
        });

        var result = new PipelineResult();

        // The producer and consumer write to disjoint sets of PipelineResult fields —
        // see PipelineResult remarks for the ownership breakdown. No synchronization is needed.
        var producerTask = ProduceAsync(channel.Writer, result, ct);
        var consumerTask = ConsumeAsync(channel.Reader, result, ct);

        await Task.WhenAll(producerTask, consumerTask);

        await MeasureQueueAsync(result, ct);

        return result;
    }

    /// <summary>
    /// Records what is still waiting for a summary after this run, so the backlog is visible as a
    /// time series rather than only at the moment someone thinks to look.
    /// </summary>
    /// <remarks>
    /// This mirrors the selection in <see cref="SummaryGenerationService.GenerateAllSummariesAsync"/>:
    /// a package is queued if it has a stale release or an empty summary row. Nothing here retries or
    /// mutates; it only counts.
    ///
    /// A failure to measure must not fail the sync. The run has already done its real work by this
    /// point, and losing one telemetry data point is not worth throwing away a successful pass.
    /// </remarks>
    private async Task MeasureQueueAsync(PipelineResult result, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

            var stalePackageIds = db.Releases
                .Where(r => r.SummaryStale)
                .Select(r => r.PackageId);

            var emptySummaryPackageIds = db.ReleaseSummaries
                .Where(s => s.Summary == null || s.Summary == "")
                .Select(s => s.PackageId);

            result.QueuedPackages = await stalePackageIds
                .Union(emptySummaryPackageIds)
                .Distinct()
                .CountAsync(ct);

            result.StaleReleases = await db.Releases.CountAsync(r => r.SummaryStale, ct);

            result.EmptySummaries = await db.ReleaseSummaries
                .CountAsync(s => s.Summary == null || s.Summary == "", ct);

            result.OldestQueuedAt = await db.Releases
                .Where(r => r.SummaryStale)
                .OrderBy(r => r.FetchedAt)
                .Select(r => (DateTimeOffset?)r.FetchedAt)
                .FirstOrDefaultAsync(ct);

            _logger.LogInformation(
                "Summary queue after run: {QueuedPackages} packages, {StaleReleases} stale releases, "
                    + "{EmptySummaries} empty summaries, oldest queued {OldestQueuedAt}",
                result.QueuedPackages, result.StaleReleases, result.EmptySummaries,
                result.OldestQueuedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to measure the summary queue; sync itself was unaffected");
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<string> writer,
        PipelineResult result,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();

            // Backfill denormalized version fields for any existing releases
            var backfilled = await syncService.BackfillVersionFieldsAsync(ct);
            if (backfilled > 0)
            {
                _logger.LogInformation("Backfilled version fields for {Count} existing releases", backfilled);
            }

            // Re-resolve stale changelogs (releases with short conventional-commits bodies)
            var reResolved = await syncService.ReResolveStaleChangelogsAsync(ct);
            result.ChangelogsReResolved = reResolved;

            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
            var packages = await db.Packages.Where(p => !p.IsSyncDisabled).ToListAsync(ct);

            _logger.LogInformation("Pipeline: syncing {Count} packages", packages.Count);

            var enqueuedPackageIds = new HashSet<string>();

            foreach (var package in packages)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Check for pre-existing stale releases BEFORE syncing,
                    // while the consumer cannot yet be modifying this package's data
                    var hadStaleReleases = await db.Releases
                        .AnyAsync(r => r.PackageId == package.Id && r.SummaryStale, ct);

                    var packageResult = await syncService.SyncPackageAsync(package, cancellationToken: ct);
                    result.PackagesSynced++;
                    result.ReleasesAdded += packageResult.ReleasesAdded;
                    if (packageResult.ReleasesAdded > 0)
                        result.PackagesWithNewReleases++;

                    if (packageResult.ReleasesAdded > 0)
                    {
                        _logger.LogInformation(
                            "Synced {Package}: {Count} new releases",
                            package.Name, packageResult.ReleasesAdded);
                    }
                    else
                    {
                        _logger.LogDebug("Synced {Package}: no new releases", package.Name);
                    }

                    // Enqueue if new releases need summaries or pre-existing stale summaries.
                    // HashSet.Add returns false if already present, preventing duplicate enqueues.
                    if ((packageResult.ReleasesNeedingSummary.Count > 0 || hadStaleReleases)
                        && enqueuedPackageIds.Add(package.Id))
                    {
                        await writer.WriteAsync(package.Id, ct);
                    }
                }
                catch (Exception ex)
                {
                    result.SyncErrors.Add(new SyncError(package.Name, ex.Message));
                    _logger.LogError(ex, "Failed to sync {Package}", package.Name);
                }
            }
        }
        finally
        {
            writer.Complete();
            _logger.LogDebug("Pipeline: producer finished");
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<string> reader,
        PipelineResult result,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var summaryService = scope.ServiceProvider.GetRequiredService<SummaryGenerationService>();

        await foreach (var packageId in reader.ReadAllAsync(ct))
        {
            try
            {
                var summaryResult = await summaryService.GenerateGroupSummariesAsync(packageId, ct);
                result.SummariesGenerated += summaryResult.SummariesGenerated;
                result.GroupsSkipped += summaryResult.GroupsSkipped;
                result.SummaryErrors.AddRange(summaryResult.Errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate summaries for package {PackageId}", packageId);
                result.SummaryErrors.Add(new SummaryGenerationError(packageId, 0, false, ex.Message));
            }
        }

        _logger.LogDebug("Pipeline: consumer finished");
    }
}
