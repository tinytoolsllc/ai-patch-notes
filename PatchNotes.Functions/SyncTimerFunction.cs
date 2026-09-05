using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PatchNotes.Sync.Core;

namespace PatchNotes.Functions;

public class SyncTimerFunction(
    SyncPipeline pipeline,
    TelemetryClient telemetryClient,
    ILogger<SyncTimerFunction> logger)
{
    // Runs every hour, on the hour
    [Function("SyncReleases")]
    public async Task Run(
        [TimerTrigger("0 0 * * * *")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Console.WriteLine($"[SyncReleases] Started at {startedAt:O}, IsPastDue: {timerInfo.IsPastDue}");
        logger.LogInformation("SyncReleases started at {Time}, IsPastDue: {IsPastDue}",
            startedAt, timerInfo.IsPastDue);

        try
        {
            var result = await pipeline.RunAsync(cancellationToken);

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            Console.WriteLine(
                $"[SyncReleases] Completed in {elapsed.TotalSeconds:F1}s — " +
                $"{result.PackagesSynced} packages, {result.ReleasesAdded} new releases, " +
                $"{result.SummariesGenerated} summaries, {result.ChangelogsReResolved} changelogs re-resolved, " +
                $"{result.SyncErrors.Count} sync errors, {result.SummaryErrors.Count} summary errors");

            logger.LogInformation(
                "SyncReleases completed in {ElapsedSeconds:F1}s — " +
                "{Packages} packages ({PackagesWithNewReleases} with new releases), {Releases} new releases, " +
                "{Summaries} summaries generated, {ChangelogsReResolved} changelogs re-resolved, " +
                "{SyncErrors} sync errors, {SummaryErrors} summary errors",
                elapsed.TotalSeconds,
                result.PackagesSynced,
                result.PackagesWithNewReleases,
                result.ReleasesAdded,
                result.SummariesGenerated,
                result.ChangelogsReResolved,
                result.SyncErrors.Count,
                result.SummaryErrors.Count);

            telemetryClient.TrackEvent("SyncReleasesCompleted", new Dictionary<string, string>
            {
                ["durationSeconds"] = elapsed.TotalSeconds.ToString("F1"),
                ["packagesSynced"] = result.PackagesSynced.ToString(),
                ["packagesWithNewReleases"] = result.PackagesWithNewReleases.ToString(),
                ["releasesAdded"] = result.ReleasesAdded.ToString(),
                ["summariesGenerated"] = result.SummariesGenerated.ToString(),
                ["changelogsReResolved"] = result.ChangelogsReResolved.ToString(),
                ["syncErrors"] = result.SyncErrors.Count.ToString(),
                ["summaryErrors"] = result.SummaryErrors.Count.ToString(),
                ["isPastDue"] = timerInfo.IsPastDue.ToString(),
            });

            // Emitted separately from SyncReleasesCompleted so the backlog can be queried and
            // alerted on without unpacking a run summary. What matters is the trend: a queue that
            // climbs run after run means summaries are failing faster than they are produced,
            // which no per-run counter reveals. This is the signal that was missing when the AI
            // provider's quota ran out and the backlog grew unnoticed for fifteen hours.
            telemetryClient.TrackEvent("SummaryQueueDepth", new Dictionary<string, string>
            {
                ["queuedPackages"] = result.QueuedPackages.ToString(),
                ["staleReleases"] = result.StaleReleases.ToString(),
                ["emptySummaries"] = result.EmptySummaries.ToString(),
                ["oldestQueuedAt"] = result.OldestQueuedAt?.ToString("O") ?? "",
                ["oldestQueuedHours"] = result.OldestQueuedAt is { } oldest
                    ? (DateTimeOffset.UtcNow - oldest).TotalHours.ToString("F1")
                    : "",
                ["summariesGenerated"] = result.SummariesGenerated.ToString(),
                ["summaryErrors"] = result.SummaryErrors.Count.ToString(),
                ["rateLimited"] = result.RateLimited.ToString(),
                ["packagesSkippedAfterRateLimit"] = result.PackagesSkippedAfterRateLimit.ToString(),
            });

            if (result.RateLimited)
            {
                logger.LogWarning(
                    "Summary generation stopped early: the AI API is rate limiting. "
                        + "{Skipped} packages were skipped and remain queued.",
                    result.PackagesSkippedAfterRateLimit);
            }

            foreach (var error in result.SyncErrors)
            {
                logger.LogError("Sync error for {Package}: {Error}", error.PackageName, error.Message);
            }

            foreach (var error in result.SummaryErrors)
            {
                logger.LogError("Summary error for package {PackageId}: {Error}", error.PackageId, error.Message);
            }
        }
        catch (Exception ex)
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            Console.WriteLine($"[SyncReleases] FAILED after {elapsed.TotalSeconds:F1}s: {ex.Message}");
            logger.LogError(ex, "SyncReleases failed after {ElapsedSeconds:F1}s",
                elapsed.TotalSeconds);

            telemetryClient.TrackEvent("SyncReleasesFailed", new Dictionary<string, string>
            {
                ["durationSeconds"] = elapsed.TotalSeconds.ToString("F1"),
                ["exceptionType"] = ex.GetType().Name,
                ["exceptionMessage"] = ex.Message,
            });

            throw;
        }
        finally
        {
            Console.WriteLine("[SyncReleases] Flushing telemetry");
            telemetryClient.Flush();
            // Give the SDK time to transmit before the process may be killed
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            Console.WriteLine("[SyncReleases] Flush complete");
        }

        if (timerInfo.ScheduleStatus is not null)
        {
            logger.LogInformation("Next SyncReleases scheduled at {NextRun}", timerInfo.ScheduleStatus.Next);
        }
    }
}
