using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

public static class AdminSummaryRoutes
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static WebApplication MapAdminSummaryRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();
        var requireAdmin = RouteUtils.CreateAdminFilter();

        var group = app.MapGroup("/api/admin/summaries")
            .WithTags("AdminSummaries")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        // GET /api/admin/summaries — summaries with operational metadata.
        //
        // The public /api/summaries endpoint returns summary text for display. This one answers
        // operational questions instead: when was it generated, how far behind is it, how many of
        // its releases are still waiting. The text itself is deliberately not returned — a page of
        // 50 summaries would be hundreds of kilobytes, and none of it helps decide what to fix.
        group.MapGet("/", async (string? packageId, int? limit, int? offset, PatchNotesDbContext db) =>
        {
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var skip = Math.Max(offset ?? 0, 0);

            var query = db.ReleaseSummaries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                query = query.Where(s => s.PackageId == packageId);
            }

            var total = await query.CountAsync();

            var summaries = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Skip(skip)
                .Take(take)
                .Select(s => new
                {
                    s.Id,
                    s.PackageId,
                    PackageName = s.Package.Name,
                    s.MajorVersion,
                    s.IsPrerelease,
                    s.Summary,
                    s.GeneratedAt,
                    s.UpdatedAt,
                })
                .ToListAsync();

            // Stale counts come from one grouped query rather than a per-row subquery, so the cost
            // does not scale with page size.
            var packageIds = summaries.Select(s => s.PackageId).Distinct().ToList();
            var staleCounts = await db.Releases
                .AsNoTracking()
                .Where(r => packageIds.Contains(r.PackageId) && r.SummaryStale)
                .GroupBy(r => new { r.PackageId, r.MajorVersion, r.IsPrerelease })
                .Select(g => new { g.Key.PackageId, g.Key.MajorVersion, g.Key.IsPrerelease, Count = g.Count() })
                .ToListAsync();

            var staleLookup = staleCounts.ToDictionary(
                c => (c.PackageId, c.MajorVersion, c.IsPrerelease),
                c => c.Count);

            var items = summaries
                .Select(s => new AdminSummaryDto
                {
                    Id = s.Id,
                    PackageId = s.PackageId,
                    PackageName = s.PackageName,
                    MajorVersion = s.MajorVersion,
                    IsPrerelease = s.IsPrerelease,
                    HasSummary = !string.IsNullOrEmpty(s.Summary),
                    SummaryLength = s.Summary != null ? s.Summary.Length : 0,
                    GeneratedAt = s.GeneratedAt,
                    UpdatedAt = s.UpdatedAt,
                    StaleReleaseCount = staleLookup.GetValueOrDefault(
                        (s.PackageId, s.MajorVersion, s.IsPrerelease), 0),
                })
                .ToList();

            return Results.Ok(new PaginatedResponse<AdminSummaryDto>
            {
                Items = items,
                Total = total,
                Limit = take,
                Offset = skip,
            });
        })
        .WithName("GetAdminSummaries");

        // GET /api/admin/summaries/queue — what is waiting for a summary, and why.
        //
        // The "queue" is not a table. It is whatever SummaryGenerationService.GenerateAllSummariesAsync
        // selects on each run: packages with a stale release, or with an empty summary row. This
        // endpoint mirrors that selection exactly, so what it reports is what the next run will attempt.
        group.MapGet("/queue", async (bool? outOfWindowOnly, int? limit, int? offset, PatchNotesDbContext db) =>
        {
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var skip = Math.Max(offset ?? 0, 0);

            var stalePackageIds = db.Releases.Where(r => r.SummaryStale).Select(r => r.PackageId);
            var emptySummaryPackageIds = db.ReleaseSummaries
                .Where(s => s.Summary == null || s.Summary == "")
                .Select(s => s.PackageId);

            var queuedPackageIds = await stalePackageIds
                .Union(emptySummaryPackageIds)
                .Distinct()
                .ToListAsync();

            if (queuedPackageIds.Count == 0)
            {
                return Results.Ok(new SummaryQueueResponse
                {
                    Items = [],
                    Total = 0,
                    Limit = take,
                    Offset = skip,
                    TotalStaleReleases = 0,
                    OutOfWindowPackages = 0,
                    OldestQueuedAt = null,
                });
            }

            // Every release for the queued packages is needed, not just the stale ones: outOfWindow
            // is measured against each version group's newest release, stale or not.
            var releases = await db.Releases
                .AsNoTracking()
                .Where(r => queuedPackageIds.Contains(r.PackageId))
                .Select(r => new
                {
                    r.PackageId,
                    r.MajorVersion,
                    r.IsPrerelease,
                    r.PublishedAt,
                    r.FetchedAt,
                    r.SummaryStale,
                })
                .ToListAsync();

            var emptySummarySet = (await db.ReleaseSummaries
                .AsNoTracking()
                .Where(s => s.Summary == null || s.Summary == "")
                .Select(s => s.PackageId)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            var packageNames = await db.Packages
                .AsNoTracking()
                .Where(p => queuedPackageIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var entries = new List<SummaryQueueEntryDto>();

            foreach (var packageId in queuedPackageIds)
            {
                var packageReleases = releases.Where(r => r.PackageId == packageId).ToList();
                var stale = packageReleases.Where(r => r.SummaryStale).ToList();
                var hasEmptySummary = emptySummarySet.Contains(packageId);

                // A stale release further back than SummaryWindow behind its own group's newest
                // release can never reach the model, so it can never change the summary. A package
                // whose staleness is entirely of that kind is queued for nothing.
                var allStaleOutOfWindow = stale.Count > 0 && stale.All(r =>
                {
                    var groupNewest = packageReleases
                        .Where(o => o.MajorVersion == r.MajorVersion && o.IsPrerelease == r.IsPrerelease)
                        .Max(o => o.PublishedAt);
                    return r.PublishedAt < groupNewest - SummaryConstants.SummaryWindow;
                });

                entries.Add(new SummaryQueueEntryDto
                {
                    PackageId = packageId,
                    PackageName = packageNames.GetValueOrDefault(packageId, "(unknown)"),
                    Reason = (stale.Count > 0, hasEmptySummary) switch
                    {
                        (true, true) => "both",
                        (true, false) => "stale-release",
                        _ => "empty-summary",
                    },
                    StaleReleaseCount = stale.Count,
                    OldestStaleReleaseAt = stale.Count > 0 ? stale.Min(r => r.PublishedAt) : null,
                    NewestStaleReleaseAt = stale.Count > 0 ? stale.Max(r => r.PublishedAt) : null,
                    QueuedSince = stale.Count > 0 ? stale.Min(r => r.FetchedAt) : null,
                    OutOfWindow = allStaleOutOfWindow,
                });
            }

            var outOfWindowCount = entries.Count(e => e.OutOfWindow);
            var totalStale = entries.Sum(e => e.StaleReleaseCount);
            var oldest = entries.Where(e => e.QueuedSince.HasValue).Select(e => e.QueuedSince!.Value);

            var filtered = outOfWindowOnly == true
                ? entries.Where(e => e.OutOfWindow).ToList()
                : entries;

            var page = filtered
                .OrderBy(e => e.QueuedSince ?? DateTimeOffset.MaxValue)
                .ThenBy(e => e.PackageName)
                .Skip(skip)
                .Take(take)
                .ToList();

            return Results.Ok(new SummaryQueueResponse
            {
                Items = page,
                Total = filtered.Count,
                Limit = take,
                Offset = skip,
                TotalStaleReleases = totalStale,
                OutOfWindowPackages = outOfWindowCount,
                OldestQueuedAt = oldest.Any() ? oldest.Min() : null,
            });
        })
        .WithName("GetSummaryQueue");

        // DELETE /api/admin/summaries/queue — drain pending work without generating anything.
        //
        // The inverse of regenerate-all: this empties the queue, that one fills it. Draining never
        // deletes a summary; it only stops releases being retried. Anything drained re-enters the
        // queue naturally the next time its package publishes a release.
        group.MapDelete("/queue", async (string? scope, string? packageId, PatchNotesDbContext db) =>
        {
            var mode = string.IsNullOrWhiteSpace(scope) ? "out-of-window" : scope.ToLowerInvariant();

            if (mode is not ("out-of-window" or "package" or "all"))
            {
                return Results.BadRequest(new
                {
                    error = "scope must be one of: out-of-window, package, all",
                });
            }

            if (mode == "package" && string.IsNullOrWhiteSpace(packageId))
            {
                return Results.BadRequest(new { error = "packageId is required when scope=package" });
            }

            var releasesCleared = 0;
            var emptySummariesDeleted = 0;

            if (mode == "all")
            {
                var stale = await db.Releases.Where(r => r.SummaryStale).ToListAsync();
                foreach (var release in stale)
                {
                    release.SummaryStale = false;
                }
                releasesCleared = stale.Count;

                var empty = await db.ReleaseSummaries
                    .Where(s => s.Summary == null || s.Summary == "")
                    .ToListAsync();
                db.ReleaseSummaries.RemoveRange(empty);
                emptySummariesDeleted = empty.Count;
            }
            else if (mode == "package")
            {
                var candidates = await db.Releases
                    .Where(r => r.SummaryStale && r.PackageId == packageId)
                    .ToListAsync();

                foreach (var release in candidates)
                {
                    release.SummaryStale = false;
                }
                releasesCleared = candidates.Count;
            }
            else
            {
                // out-of-window: only releases that cannot reach the model. Measured against each
                // version group's newest release, which is why every release for the affected
                // packages is loaded, not just the stale ones.
                //
                // This uses SummaryWindow rather than the wider StaleReleaseCutoff the sync job
                // clears at, so a drain removes exactly the entries the queue endpoint reports as
                // outOfWindow. A manual drain is allowed to be more aggressive than the automatic
                // one; the two would otherwise disagree about what is drainable.
                var candidates = await db.Releases.Where(r => r.SummaryStale).ToListAsync();
                var affectedPackageIds = candidates.Select(r => r.PackageId).Distinct().ToList();

                var allReleases = await db.Releases
                    .AsNoTracking()
                    .Where(r => affectedPackageIds.Contains(r.PackageId))
                    .Select(r => new { r.PackageId, r.MajorVersion, r.IsPrerelease, r.PublishedAt })
                    .ToListAsync();

                foreach (var release in candidates)
                {
                    var groupNewest = allReleases
                        .Where(o => o.PackageId == release.PackageId
                            && o.MajorVersion == release.MajorVersion
                            && o.IsPrerelease == release.IsPrerelease)
                        .Max(o => o.PublishedAt);

                    if (release.PublishedAt < groupNewest - SummaryConstants.SummaryWindow)
                    {
                        release.SummaryStale = false;
                        releasesCleared++;
                    }
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new DrainQueueResult
            {
                Scope = mode,
                ReleasesCleared = releasesCleared,
                EmptySummariesDeleted = emptySummariesDeleted,
            });
        })
        .WithName("DrainSummaryQueue");

        // POST /api/admin/summaries/regenerate-all — delete every summary and re-queue everything.
        //
        // Requires ?confirm=true. Without it the endpoint reports what it would do and changes
        // nothing.
        //
        // That guard is not ceremony. This deletes every ReleaseSummary and marks every release
        // stale, so until generation catches up the site and every digest show no summary text at
        // all. If the AI provider happens to be refusing — an exhausted quota, say — nothing
        // regenerates and the summaries simply stay gone. Seeing the blast radius before causing
        // it is the difference between a maintenance action and an outage.
        group.MapPost("/regenerate-all", async (bool? confirm, PatchNotesDbContext db) =>
        {
            var summaryCount = await db.ReleaseSummaries.CountAsync();
            var releaseCount = await db.Releases.CountAsync();
            var alreadyStale = await db.Releases.CountAsync(r => r.SummaryStale);

            if (confirm != true)
            {
                return Results.BadRequest(new RegenerateAllResult
                {
                    Confirmed = false,
                    SummariesDeleted = summaryCount,
                    ReleasesMarkedStale = releaseCount - alreadyStale,
                    TotalReleases = releaseCount,
                    Message = "Nothing changed. Re-send with confirm=true to proceed. Until "
                        + "generation catches up, no summary text is shown anywhere.",
                });
            }

            var summaries = await db.ReleaseSummaries.ToListAsync();
            db.ReleaseSummaries.RemoveRange(summaries);

            var releases = await db.Releases.Where(r => !r.SummaryStale).ToListAsync();
            foreach (var release in releases)
            {
                release.SummaryStale = true;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new RegenerateAllResult
            {
                Confirmed = true,
                SummariesDeleted = summaries.Count,
                ReleasesMarkedStale = releases.Count,
                TotalReleases = releaseCount,
                Message = "Every summary was deleted and every release queued for regeneration.",
            });
        })
        .WithName("RegenerateAllSummaries");

        return app;
    }
}

public class DrainQueueResult
{
    /// <summary>The scope that was applied: out-of-window, package, or all.</summary>
    public required string Scope { get; set; }

    /// <summary>Releases whose stale flag was cleared, so they are no longer queued.</summary>
    public int ReleasesCleared { get; set; }

    /// <summary>Empty summary rows removed. Only scope=all deletes these.</summary>
    public int EmptySummariesDeleted { get; set; }
}

public class RegenerateAllResult
{
    /// <summary>False when this was a preview and nothing was changed.</summary>
    public bool Confirmed { get; set; }

    /// <summary>Summaries deleted, or that would be deleted on an unconfirmed call.</summary>
    public int SummariesDeleted { get; set; }

    /// <summary>Releases newly marked stale, excluding those already queued.</summary>
    public int ReleasesMarkedStale { get; set; }

    public int TotalReleases { get; set; }
    public required string Message { get; set; }
}

public class AdminSummaryDto
{
    public required string Id { get; set; }
    public required string PackageId { get; set; }
    public required string PackageName { get; set; }
    public int MajorVersion { get; set; }
    public bool IsPrerelease { get; set; }

    /// <summary>False when generation has failed and left an empty row behind.</summary>
    public bool HasSummary { get; set; }
    public int SummaryLength { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Releases in this version group still waiting to be summarized.</summary>
    public int StaleReleaseCount { get; set; }
}

public class SummaryQueueEntryDto
{
    public required string PackageId { get; set; }
    public required string PackageName { get; set; }

    /// <summary>Why this package is queued: stale-release, empty-summary, or both.</summary>
    public required string Reason { get; set; }

    public int StaleReleaseCount { get; set; }
    public DateTimeOffset? OldestStaleReleaseAt { get; set; }
    public DateTimeOffset? NewestStaleReleaseAt { get; set; }

    /// <summary>When the oldest stale release was fetched — how long this has been waiting.</summary>
    public DateTimeOffset? QueuedSince { get; set; }

    /// <summary>
    /// Every stale release sits further back than SummaryConstants.SummaryWindow behind its own
    /// version group's newest, so none of them can reach the model. Regenerating would reproduce
    /// the existing text, which makes these entries safe to drain.
    /// </summary>
    public bool OutOfWindow { get; set; }
}

public class SummaryQueueResponse
{
    public required List<SummaryQueueEntryDto> Items { get; set; }
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }

    /// <summary>Stale releases across the whole queue, not just this page.</summary>
    public int TotalStaleReleases { get; set; }

    /// <summary>Queued packages whose staleness cannot affect any summary.</summary>
    public int OutOfWindowPackages { get; set; }

    /// <summary>Oldest entry in the whole queue, as a fetch time.</summary>
    public DateTimeOffset? OldestQueuedAt { get; set; }
}
