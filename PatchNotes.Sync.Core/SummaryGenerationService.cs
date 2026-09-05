using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchNotes.Data;
using PatchNotes.Sync.Core.AI;

namespace PatchNotes.Sync.Core;

/// <summary>
/// Generates AI summaries per version group, aggregating release notes
/// within each group and upserting ReleaseSummary records.
/// </summary>
public class SummaryGenerationService
{
    private static readonly TimeSpan SummaryWindow = SummaryConstants.SummaryWindow;

    /// <summary>
    /// Maximum characters per release body sent to the AI API.
    /// Prevents token limit errors for packages with very long release notes.
    /// </summary>
    private const int MaxBodyCharsPerRelease = 4000;

    private readonly PatchNotesDbContext _db;
    private readonly IAiClient _aiClient;
    private readonly VersionGroupingService _groupingService;
    private readonly ILogger<SummaryGenerationService> _logger;

    public SummaryGenerationService(
        PatchNotesDbContext db,
        IAiClient aiClient,
        VersionGroupingService groupingService,
        ILogger<SummaryGenerationService> logger)
    {
        _db = db;
        _aiClient = aiClient;
        _groupingService = groupingService;
        _logger = logger;
    }

    /// <summary>
    /// Generates summaries for all version groups of a package that have new or stale releases.
    /// </summary>
    public async Task<SummaryGenerationResult> GenerateGroupSummariesAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var result = new SummaryGenerationResult();

        var package = await _db.Packages.FindAsync([packageId], cancellationToken);
        if (package == null)
        {
            _logger.LogWarning("Package {PackageId} not found", packageId);
            return result;
        }

        var releases = await _db.Releases
            .Where(r => r.PackageId == packageId)
            .ToListAsync(cancellationToken);

        if (releases.Count == 0)
        {
            _logger.LogDebug("No releases found for package {PackageId}", packageId);
            return result;
        }

        var groups = _groupingService.GroupReleases(releases).ToList();

        // Load existing summaries for this package
        var existingSummaries = await _db.ReleaseSummaries
            .Where(s => s.PackageId == packageId)
            .ToDictionaryAsync(
                s => (s.MajorVersion, s.IsPrerelease),
                cancellationToken);

        // Remove orphaned summaries (null from a previous failed generation) so they get recreated
        var orphanedKeys = new HashSet<(int, bool)>();
        foreach (var (key, summary) in existingSummaries.Where(kv => string.IsNullOrEmpty(kv.Value.Summary)).ToList())
        {
            _db.ReleaseSummaries.Remove(summary);
            existingSummaries.Remove(key);
            orphanedKeys.Add(key);
        }

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Regenerate if the group has stale releases or had a previously failed summary
            var hasStaleReleases = group.Releases.Any(r => r.SummaryStale);
            if (!hasStaleReleases && !orphanedKeys.Contains((group.MajorVersion, group.IsPrerelease)))
            {
                result.GroupsSkipped++;
                continue;
            }

            try
            {
                var summary = await GenerateGroupSummaryAsync(package.Name, group, cancellationToken);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    result.GroupsSkipped++;
                    continue;
                }

                // Upsert ReleaseSummary
                var key = (group.MajorVersion, group.IsPrerelease);
                if (existingSummaries.TryGetValue(key, out var existing))
                {
                    existing.Summary = summary;
                }
                else
                {
                    var releaseSummary = new ReleaseSummary
                    {
                        PackageId = packageId,
                        MajorVersion = group.MajorVersion,
                        IsPrerelease = group.IsPrerelease,
                        Summary = summary,
                        GeneratedAt = DateTimeOffset.UtcNow
                    };
                    _db.ReleaseSummaries.Add(releaseSummary);
                    existingSummaries[key] = releaseSummary;
                }

                // Mark releases in this group as no longer stale
                foreach (var release in group.Releases.Where(r => r.SummaryStale))
                {
                    release.SummaryStale = false;
                }

                result.SummariesGenerated++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Non-retryable error (e.g., payload too large for model context).
                // Mark releases as not stale to break the infinite retry loop.
                _logger.LogWarning(ex,
                    "AI API returned 400 for package {PackageId} v{MajorVersion} (prerelease={IsPrerelease}). " +
                    "Marking releases as not stale to stop retrying.",
                    packageId, group.MajorVersion, group.IsPrerelease);

                foreach (var release in group.Releases.Where(r => r.SummaryStale))
                {
                    release.SummaryStale = false;
                }

                result.Errors.Add(new SummaryGenerationError(
                    packageId, group.MajorVersion, group.IsPrerelease, ex.Message));
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Quota or rate limit. Unlike the 400 above this is not the payload's fault, so the
                // releases stay stale and will be retried once quota returns. What must not happen is
                // grinding through the rest of the backlog: every remaining call this run will be
                // refused as well, and each one burns an attempt against the limit that is already
                // exhausted. Stop here and let the caller stop too.
                _logger.LogWarning(ex,
                    "AI API returned 429 for package {PackageId} v{MajorVersion} (prerelease={IsPrerelease}). "
                        + "Stopping summary generation for this run; releases stay queued.",
                    packageId, group.MajorVersion, group.IsPrerelease);

                result.RateLimited = true;
                result.Errors.Add(new SummaryGenerationError(
                    packageId, group.MajorVersion, group.IsPrerelease, ex.Message));
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate summary for package {PackageId} v{MajorVersion} (prerelease={IsPrerelease})",
                    packageId, group.MajorVersion, group.IsPrerelease);
                result.Errors.Add(new SummaryGenerationError(
                    packageId, group.MajorVersion, group.IsPrerelease, ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Summary generation for package {PackageId}: {Generated} generated, {Skipped} skipped, {Errors} errors",
            packageId, result.SummariesGenerated, result.GroupsSkipped, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Generates summaries for all packages that have releases needing summaries.
    /// </summary>
    public async Task<SummaryGenerationResult> GenerateAllSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var aggregateResult = new SummaryGenerationResult();

        // Find all packages that have at least one stale release or a failed summary
        var staleReleasePackageIds = _db.Releases
            .Where(r => r.SummaryStale)
            .Select(r => r.PackageId);

        var orphanedSummaryPackageIds = _db.ReleaseSummaries
            .Where(s => s.Summary == null || s.Summary == "")
            .Select(s => s.PackageId);

        var packageIds = await staleReleasePackageIds
            .Union(orphanedSummaryPackageIds)
            .Distinct()
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Generating summaries for {Count} packages with stale releases", packageIds.Count);

        foreach (var packageId in packageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await GenerateGroupSummariesAsync(packageId, cancellationToken);
            aggregateResult.SummariesGenerated += result.SummariesGenerated;
            aggregateResult.GroupsSkipped += result.GroupsSkipped;
            aggregateResult.Errors.AddRange(result.Errors);

            if (result.RateLimited)
            {
                aggregateResult.RateLimited = true;
                _logger.LogWarning(
                    "Stopping after {Generated} summaries: the AI API is rate limiting. "
                        + "{Remaining} packages remain queued for the next run.",
                    aggregateResult.SummariesGenerated,
                    packageIds.Count - packageIds.IndexOf(packageId) - 1);
                break;
            }
        }

        return aggregateResult;
    }

    private async Task<string> GenerateGroupSummaryAsync(
        string packageName,
        ReleaseVersionGroup group,
        CancellationToken cancellationToken)
    {
        var ordered = group.Releases
            .OrderByDescending(r => r.PublishedAt)
            .ToList();

        var cutoff = ordered.First().PublishedAt - SummaryWindow;

        var recentReleases = ordered
            .Where(r => r.PublishedAt >= cutoff)
            .ToList();

        // Fall back to the latest release if none are within the window
        if (recentReleases.Count == 0)
        {
            recentReleases = ordered.Take(1).ToList();
        }

        var releaseInputs = recentReleases
            .Select(r => new ReleaseInput(r.Tag, r.Title, Truncate(r.Body, MaxBodyCharsPerRelease), r.PublishedAt))
            .ToList();

        return await _aiClient.SummarizeReleaseNotesAsync(packageName, releaseInputs, cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;

        return string.Concat(value.AsSpan(0, maxLength), "\n\n[truncated]");
    }
}
