namespace PatchNotes.Data;

public static class SummaryConstants
{
    /// <summary>
    /// Maximum time window of releases to include in a single summary.
    /// Used by both summary generation and feed display to ensure consistency.
    /// </summary>
    public static readonly TimeSpan SummaryWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// How far behind a version group's newest release a stale release can sit before it stops
    /// being worth regenerating for.
    /// </summary>
    /// <remarks>
    /// Only releases within <see cref="SummaryWindow"/> of the group's newest reach the model, so a
    /// release further back than that cannot change the summary text no matter how many times it is
    /// retried — it just keeps its package in the generation queue. Re-resolving an old changelog is
    /// the common way this happens: it marks a months-old release stale, which queues the package
    /// for a summary the release cannot appear in.
    ///
    /// Deliberately wider than <see cref="SummaryWindow"/>. The two days of slack mean a release near
    /// the boundary is never dropped from the queue while it could still legitimately contribute.
    /// </remarks>
    public static readonly TimeSpan StaleReleaseCutoff = TimeSpan.FromDays(9);
}
