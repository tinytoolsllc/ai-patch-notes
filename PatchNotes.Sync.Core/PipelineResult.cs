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

    public bool Success => SyncErrors.Count == 0 && SummaryErrors.Count == 0;
}
