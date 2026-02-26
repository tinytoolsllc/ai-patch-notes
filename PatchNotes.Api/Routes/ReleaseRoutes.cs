using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;

namespace PatchNotes.Api.Routes;

public static class ReleaseRoutes
{
    public static WebApplication MapReleaseRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();

        var group = app.MapGroup("/api/releases").WithTags("Releases");

        // GET /api/releases/{id} - Get single release details
        group.MapGet("/{id}", async (string id, PatchNotesDbContext db) =>
        {
            var release = await db.Releases
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new ReleaseDto
                {
                    Id = r.Id,
                    Tag = r.Tag,
                    Title = r.Title,
                    Body = r.Body,
                    PublishedAt = r.PublishedAt,
                    FetchedAt = r.FetchedAt,
                    Package = new ReleasePackageDto
                    {
                        Id = r.Package.Id,
                        NpmName = r.Package.NpmName,
                        GithubOwner = r.Package.GithubOwner,
                        GithubRepo = r.Package.GithubRepo
                    }
                })
                .FirstOrDefaultAsync();

            if (release == null)
            {
                return Results.NotFound(new ApiError("Release not found"));
            }

            return Results.Ok(release);
        })
        .Produces<ReleaseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetRelease");

        // GET /api/releases - Query releases for selected packages
        group.MapGet("/", async (string? packages, int? days, bool? excludePrerelease, int? majorVersion,
            bool? watchlist, int? limit, int? offset, HttpContext httpContext, PatchNotesDbContext db, IStytchClient stytchClient,
            IOptions<DefaultWatchlistOptions> watchlistOptions) =>
        {
            var daysToQuery = days ?? 7;
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-daysToQuery);

            IQueryable<Release> query = db.Releases
                .AsNoTracking()
                .Where(r => r.PublishedAt >= cutoffDate);

            if (watchlist == true && !string.IsNullOrEmpty(packages))
            {
                return Results.Json(new ApiError("Cannot specify both 'watchlist' and 'packages' parameters"), statusCode: 400);
            }

            if (watchlist == true)
            {
                // Explicit watchlist filter — require authentication
                var userWatchlistIds = await RouteUtils.GetAuthenticatedUserWatchlistIds(httpContext, db, stytchClient);
                if (userWatchlistIds == null)
                {
                    return Results.Json(new ApiError("Authentication required for watchlist filter"), statusCode: 401);
                }

                query = query.Where(r => userWatchlistIds.Contains(r.PackageId));
            }
            else if (!string.IsNullOrEmpty(packages))
            {
                var packageIds = packages.Split(',')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (packageIds.Count > 0)
                {
                    query = query.Where(r => packageIds.Contains(r.PackageId));
                }
            }
            else
            {
                // No explicit packages filter — use watchlist
                var (watchlistIds, hasWatchlistConfig) = await RouteUtils.ResolveWatchlistPackageIds(
                    httpContext, db, stytchClient, watchlistOptions.Value);

                if (hasWatchlistConfig)
                {
                    // Watchlist is configured — filter to it (empty watchlist = empty results)
                    query = query.Where(r => watchlistIds.Contains(r.PackageId));
                }
                // else: no watchlist configured at all — show all releases
            }

            // Filter using denormalized version fields at DB level
            if (excludePrerelease == true)
            {
                query = query.Where(r => !r.IsPrerelease);
            }

            if (majorVersion.HasValue)
            {
                query = query.Where(r => r.MajorVersion == majorVersion.Value);
            }

            var take = Math.Clamp(limit ?? 20, 1, 100);
            var skip = Math.Max(offset ?? 0, 0);

            var total = await query.CountAsync();

            var releases = await query
                .OrderByDescending(r => r.PublishedAt)
                .Skip(skip)
                .Take(take)
                .Select(r => new ReleaseDto
                {
                    Id = r.Id,
                    Tag = r.Tag,
                    Title = r.Title,
                    Body = r.Body,
                    PublishedAt = r.PublishedAt,
                    FetchedAt = r.FetchedAt,
                    Package = new ReleasePackageDto
                    {
                        Id = r.Package.Id,
                        NpmName = r.Package.NpmName,
                        GithubOwner = r.Package.GithubOwner,
                        GithubRepo = r.Package.GithubRepo
                    }
                })
                .ToListAsync();

            return Results.Ok(new PaginatedResponse<ReleaseDto>
            {
                Items = releases,
                Total = total,
                Limit = take,
                Offset = skip
            });
        })
        .Produces<PaginatedResponse<ReleaseDto>>(StatusCodes.Status200OK)
        .WithName("GetReleases");

        return app;
    }

}

public class ReleasePackageDto
{
    public required string Id { get; set; }
    public string? NpmName { get; set; }
    public required string GithubOwner { get; set; }
    public required string GithubRepo { get; set; }
}

public class ReleaseDto
{
    public required string Id { get; set; }
    public required string Tag { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public required ReleasePackageDto Package { get; set; }
}
