using PatchNotes.Data;

namespace PatchNotes.Sync.Core;

/// <summary>
/// Result of syncing all packages.
/// </summary>
public record SyncResult
{
    public int PackagesSynced { get; internal set; }
    public int ReleasesAdded { get; internal set; }
    public List<SyncError> Errors { get; } = [];

    /// <summary>
    /// Releases that need summary generation (new releases or releases without summaries).
    /// </summary>
    public List<Release> ReleasesNeedingSummary { get; } = [];

    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Result of syncing a single package.
/// </summary>
public record PackageSyncResult
{
    public int ReleasesAdded { get; init; }

    /// <summary>
    /// Releases from this package that need summary generation.
    /// </summary>
    public List<Release> ReleasesNeedingSummary { get; init; } = [];

    public PackageSyncResult(int releasesAdded) => ReleasesAdded = releasesAdded;

    public PackageSyncResult(int releasesAdded, List<Release> releasesNeedingSummary)
    {
        ReleasesAdded = releasesAdded;
        ReleasesNeedingSummary = releasesNeedingSummary;
    }
}

/// <summary>
/// Error that occurred during sync.
/// </summary>
public record SyncError(string PackageName, string Message);

/// <summary>
/// Result of generating version group summaries.
/// </summary>
public record SummaryGenerationResult
{
    public int SummariesGenerated { get; internal set; }
    public int GroupsSkipped { get; internal set; }
    public List<SummaryGenerationError> Errors { get; } = [];

    /// <summary>
    /// The AI provider refused with 429. Every further call this run will be refused too, so callers
    /// should stop rather than work through the rest of the backlog against a closed door.
    /// </summary>
    /// <remarks>
    /// Deliberately distinct from a normal error: the work is still valid and stays queued. Releases
    /// keep <c>SummaryStale = true</c> so they are retried once quota returns, unlike the 400 path,
    /// which clears the flag because a rejected payload will never succeed.
    /// </remarks>
    public bool RateLimited { get; internal set; }

    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Error that occurred during summary generation.
/// </summary>
public record SummaryGenerationError(
    string PackageId, int MajorVersion, bool IsPrerelease, string Message);
