using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

public static class SummaryRoutes
{
    public static WebApplication MapSummaryRoutes(this WebApplication app)
    {
        var summariesGroup = app.MapGroup("/api").WithTags("Summaries");

        // GET /api/packages/{id}/summaries - Get all release summaries for a package
        summariesGroup.MapGet("/packages/{id}/summaries", async (
            string id,
            bool? includePrerelease,
            int? majorVersion,
            PatchNotesDbContext db) =>
        {
            var packageExists = await db.Packages.AnyAsync(p => p.Id == id);
            if (!packageExists)
            {
                return Results.NotFound(new ApiError("Package not found"));
            }

            IQueryable<ReleaseSummary> query = db.ReleaseSummaries
                .AsNoTracking()
                .Where(s => s.PackageId == id);

            if (includePrerelease != true)
            {
                query = query.Where(s => !s.IsPrerelease);
            }

            if (majorVersion.HasValue)
            {
                query = query.Where(s => s.MajorVersion == majorVersion.Value);
            }

            var summaries = await query
                .OrderByDescending(s => s.MajorVersion)
                .Select(s => new ReleaseSummaryDto
                {
                    Id = s.Id,
                    PackageId = s.PackageId,
                    PackageName = s.Package.Name,
                    MajorVersion = s.MajorVersion,
                    IsPrerelease = s.IsPrerelease,
                    Summary = s.Summary,
                    GeneratedAt = s.GeneratedAt,
                    UpdatedAt = s.UpdatedAt
                })
                .ToListAsync();

            return Results.Ok(summaries);
        })
        .Produces<List<ReleaseSummaryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetPackageSummaries");

        // GET /api/summaries - Get summaries across all packages (or filtered)
        summariesGroup.MapGet("/summaries", async (
            string? packages,
            bool? includePrerelease,
            int? limit,
            PatchNotesDbContext db) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);

            IQueryable<ReleaseSummary> query = db.ReleaseSummaries
                .AsNoTracking();

            if (!string.IsNullOrEmpty(packages))
            {
                var packageIds = packages.Split(',')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (packageIds.Count > 0)
                {
                    query = query.Where(s => packageIds.Contains(s.PackageId));
                }
            }

            if (includePrerelease != true)
            {
                query = query.Where(s => !s.IsPrerelease);
            }

            var summaries = await query
                .OrderByDescending(s => s.MajorVersion)
                .Take(take)
                .Select(s => new ReleaseSummaryDto
                {
                    Id = s.Id,
                    PackageId = s.PackageId,
                    PackageName = s.Package.Name,
                    MajorVersion = s.MajorVersion,
                    IsPrerelease = s.IsPrerelease,
                    Summary = s.Summary,
                    GeneratedAt = s.GeneratedAt,
                    UpdatedAt = s.UpdatedAt
                })
                .ToListAsync();

            return Results.Ok(summaries);
        })
        .Produces<List<ReleaseSummaryDto>>(StatusCodes.Status200OK)
        .WithName("GetSummaries");

        return app;
    }
}

public class ReleaseSummaryDto
{
    public required string Id { get; set; }
    public required string PackageId { get; set; }
    public required string PackageName { get; set; }
    public int MajorVersion { get; set; }
    public bool IsPrerelease { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
