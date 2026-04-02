using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;
using static PatchNotes.Data.SummaryConstants;

namespace PatchNotes.Api.Routes;

public static class FeedRoutes
{
    public static WebApplication MapFeedRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/feed").WithTags("Feed");

        // GET /api/feed - Combined feed with server-side grouping and summaries
        group.MapGet("/", async (
            bool? excludePrerelease,
            HttpContext httpContext,
            PatchNotesDbContext db,
            IStytchClient stytchClient,
            IMemoryCache cache) =>
        {
            // Check cache first; authenticate only if needed
            var cacheKey = $"feed:default:{excludePrerelease ?? false}";
            if (cache.TryGetValue(cacheKey, out FeedResponseDto? cached))
                return Results.Ok(cached);

            // Resolve which packages to show: user's watchlist or top 5 most recent
            var userWatchlistIds = await RouteUtils.GetAuthenticatedUserWatchlistIds(
                httpContext, db, stytchClient);

            var isDefaultFeed = userWatchlistIds is not { Count: > 0 };
            List<string> watchlistIds;

            if (!isDefaultFeed)
            {
                watchlistIds = userWatchlistIds!;
            }
            else
            {
                // No auth or empty watchlist: fetch a wider pool of packages so that
                // individual (package, track) groups can compete for the top 5 slots.
                watchlistIds = await db.Releases
                    .AsNoTracking()
                    .GroupBy(r => r.PackageId)
                    .Select(g => new { PackageId = g.Key, LatestRelease = g.Max(r => r.PublishedAt) })
                    .OrderByDescending(x => x.LatestRelease)
                    .Take(10)
                    .Select(x => x.PackageId)
                    .ToListAsync();
            }

            // Fetch package metadata separately to avoid correlated subqueries
            var packageLookup = await db.Packages
                .AsNoTracking()
                .Where(p => watchlistIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.NpmName, p.GithubOwner, p.GithubRepo })
                .ToDictionaryAsync(p => p.Id);

            IQueryable<Release> releaseQuery = db.Releases
                .AsNoTracking()
                .Where(r => watchlistIds.Contains(r.PackageId));

            if (excludePrerelease == true)
            {
                releaseQuery = releaseQuery.Where(r => !r.IsPrerelease);
            }

            // Group releases server-side by (packageId, majorVersion, isPrerelease)
            var groups = await releaseQuery
                .GroupBy(r => new { r.PackageId, r.MajorVersion, r.IsPrerelease })
                .Select(g => new
                {
                    g.Key.PackageId,
                    g.Key.MajorVersion,
                    g.Key.IsPrerelease,
                    ReleaseCount = g.Count(),
                    LastUpdated = g.Max(r => r.PublishedAt),
                    Releases = g.OrderByDescending(r => r.PublishedAt)
                        .Select(r => new FeedReleaseDto
                        {
                            Id = r.Id,
                            Tag = r.Tag,
                            Title = r.Title,
                            PublishedAt = r.PublishedAt,
                        })
                        .ToList(),
                })
                .OrderByDescending(g => g.LastUpdated)
                .ToListAsync();

            // Filter to current stable + future pre-releases per package.
            // For each package: keep the highest stable major version group,
            // plus any pre-release groups with a higher major version.
            // If no stable releases exist, keep the highest major pre-release group.
            var maxStableByPackage = groups
                .Where(g => !g.IsPrerelease)
                .GroupBy(g => g.PackageId)
                .ToDictionary(pg => pg.Key, pg => pg.Max(g => g.MajorVersion));

            var filteredGroups = groups.Where(g =>
            {
                if (maxStableByPackage.TryGetValue(g.PackageId, out var maxStable))
                {
                    // Has stable releases: keep current stable + prereleases at same or higher major
                    return (!g.IsPrerelease && g.MajorVersion == maxStable)
                        || (g.IsPrerelease && g.MajorVersion >= maxStable);
                }
                // No stable releases: handled below
                return false;
            }).ToList();

            // For packages with no stable releases, add highest major prerelease group
            var packagesWithNoStable = groups
                .Select(g => g.PackageId)
                .Distinct()
                .Where(pid => !maxStableByPackage.ContainsKey(pid));
            foreach (var pid in packagesWithNoStable)
            {
                var packageGroups = groups.Where(g => g.PackageId == pid).ToList();
                var maxMajor = packageGroups.Max(g => g.MajorVersion);
                filteredGroups.AddRange(packageGroups.Where(g => g.MajorVersion == maxMajor));
            }

            // Default feed: each (package, major, prerelease) group competes
            // independently for a top-5 slot based on its most recent release.
            if (isDefaultFeed)
            {
                filteredGroups = filteredGroups
                    .OrderByDescending(g => g.LastUpdated)
                    .Take(5)
                    .ToList();
            }

            // Left-join ReleaseSummary to attach AI summaries per group
            var groupKeys = filteredGroups.Select(g => new { g.PackageId, g.MajorVersion, g.IsPrerelease }).ToList();

            var summaryPackageIds = groupKeys.Select(k => k.PackageId).Distinct().ToList();
            var summaryMajorVersions = groupKeys.Select(k => k.MajorVersion).Distinct().ToList();

            var summaryLookup = (await db.ReleaseSummaries
                .AsNoTracking()
                .Where(s => summaryPackageIds.Contains(s.PackageId)
                    && summaryMajorVersions.Contains(s.MajorVersion))
                .Select(s => new { s.PackageId, s.MajorVersion, s.IsPrerelease, s.Summary })
                .ToListAsync())
                .ToDictionary(
                    s => (s.PackageId, s.MajorVersion, s.IsPrerelease),
                    s => s.Summary);

            var feedGroups = filteredGroups.Select(g =>
            {
                summaryLookup.TryGetValue((g.PackageId, g.MajorVersion, g.IsPrerelease), out var summary);
                packageLookup.TryGetValue(g.PackageId, out var pkg);

                // Limit displayed releases to the same window used for summary generation
                var cutoff = g.LastUpdated - SummaryWindow;
                var windowedReleases = g.Releases
                    .Where(r => r.PublishedAt >= cutoff)
                    .ToList();
                if (windowedReleases.Count == 0)
                    windowedReleases = g.Releases.Take(1).ToList();

                return new FeedGroupDto
                {
                    PackageId = g.PackageId,
                    PackageName = pkg?.Name ?? g.PackageId,
                    NpmName = pkg?.NpmName,
                    GithubOwner = pkg?.GithubOwner ?? "",
                    GithubRepo = pkg?.GithubRepo ?? "",
                    MajorVersion = g.MajorVersion,
                    IsPrerelease = g.IsPrerelease,
                    VersionRange = $"v{g.MajorVersion}.x",
                    Summary = summary,
                    ReleaseCount = g.ReleaseCount,
                    LastUpdated = g.LastUpdated,
                    Releases = windowedReleases,
                };
            }).ToList();

            var response = new FeedResponseDto { Groups = feedGroups, IsDefaultFeed = isDefaultFeed };

            if (isDefaultFeed)
            {
                cache.Set(cacheKey, response, TimeSpan.FromSeconds(60));
            }

            return Results.Ok(response);
        })
        .Produces<FeedResponseDto>(StatusCodes.Status200OK)
        .WithName("GetFeed");

        return app;
    }
}

public class FeedResponseDto
{
    public required List<FeedGroupDto> Groups { get; set; }
    public bool IsDefaultFeed { get; set; }
}

public class FeedGroupDto
{
    public required string PackageId { get; set; }
    public required string PackageName { get; set; }
    public string? NpmName { get; set; }
    public required string GithubOwner { get; set; }
    public required string GithubRepo { get; set; }
    public int MajorVersion { get; set; }
    public bool IsPrerelease { get; set; }
    public required string VersionRange { get; set; }
    public string? Summary { get; set; }
    public int ReleaseCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public required List<FeedReleaseDto> Releases { get; set; }
}

public class FeedReleaseDto
{
    public required string Id { get; set; }
    public required string Tag { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}
