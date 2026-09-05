namespace PatchNotes.Sync.Core;

/// <summary>
/// Combined result from the sync pipeline (producer + consumer).
/// </summary>
/// <remarks>
/// Thread ownership: the producer task exclusively writes <see cref="PackagesSynced"/>,
/// <see cref="PackagesWithNewReleases"/>, <see cref="ReleasesAdded"/>, and <see cref="SyncErrors"/>.
/// The consumer task exclusively writes <see cref="SummariesGenerated"/>, <see cref="GroupsSkipped"/>,
/// and <see cref="SummaryErrors"/>. Because these sets of fields do not overlap, no synchronization
/// is required. Adding cross-task writes to any of these fields would introduce a data race.
///
/// The queue-depth fields are the exception: they are written by <see cref="SyncPipeline.RunAsync"/>
/// itself, after both tasks have completed, so they are not part of the ownership split above.
/// </remarks>
public record PipelineResult
{
    // Written exclusively by the producer task (SyncPipeline.ProduceAsync)
    public int PackagesSynced { get; internal set; }
    public int PackagesWithNewReleases { get; internal set; }
    public int ReleasesAdded { get; internal set; }
    public int ChangelogsReResolved { get; internal set; }
    public List<SyncError> SyncErrors { get; } = [];

    // Written exclusively by the consumer task (SyncPipeline.ConsumeAsync)
    public int SummariesGenerated { get; internal set; }
    public int GroupsSkipped { get; internal set; }
    public List<SummaryGenerationError> SummaryErrors { get; } = [];

    /// <summary>The AI API refused with 429 and summary generation stopped early this run.</summary>
    public bool RateLimited { get; internal set; }

    /// <summary>Packages the consumer read but did not attempt, because it had already stopped.</summary>
    public int PackagesSkippedAfterRateLimit { get; internal set; }

    // Written by SyncPipeline.RunAsync after both tasks complete — a snapshot of what is still
    // waiting for a summary once this run finished. Growth across runs means summaries are failing
    // faster than they are being produced, which is invisible from the per-run counters above.
    public int QueuedPackages { get; internal set; }
    public int StaleReleases { get; internal set; }
    public int EmptySummaries { get; internal set; }
    public DateTimeOffset? OldestQueuedAt { get; internal set; }

    public bool Success => SyncErrors.Count == 0 && SummaryErrors.Count == 0;
}
