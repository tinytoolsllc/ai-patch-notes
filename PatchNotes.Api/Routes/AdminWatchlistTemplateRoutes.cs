using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

/// <summary>
/// Management for the curated watchlist templates offered during onboarding.
/// </summary>
/// <remarks>
/// Reading them is already public at GET /api/watchlist/templates, because onboarding needs it.
/// Creating and editing them had no endpoint at all, so the lists new users are offered could only
/// be changed by editing the database directly.
/// </remarks>
public static class AdminWatchlistTemplateRoutes
{
    public static WebApplication MapAdminWatchlistTemplateRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();
        var requireAdmin = RouteUtils.CreateAdminFilter();

        // Management sits on the same path as the public read, not a parallel /api/admin one.
        // A template is one resource; splitting read from write across two paths would mean two
        // places to look and two contracts to keep aligned. The read stays public because
        // onboarding needs it; everything here is admin-gated per route.
        var group = app.MapGroup("/api/watchlist/templates")
            .WithTags("AdminWatchlistTemplates")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        group.MapPost("/", async (CreateTemplateRequest request, PatchNotesDbContext db) =>
        {
            var name = request.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new ApiError("Name is required"));
            }

            if (await db.WatchlistTemplates.AnyAsync(t => t.Name == name))
            {
                return Results.Conflict(new ApiError($"A template named '{name}' already exists"));
            }

            var template = new WatchlistTemplate
            {
                Name = name,
                Description = request.Description?.Trim() ?? "",
                SortOrder = request.SortOrder ?? 0,
            };

            db.WatchlistTemplates.Add(template);
            await db.SaveChangesAsync();

            return Results.Created($"/api/watchlist/templates/{template.Id}", new AdminTemplateDto
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                SortOrder = template.SortOrder,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
                PackageCount = 0,
                Packages = [],
            });
        })
        .Produces<AdminTemplateDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .WithName("CreateWatchlistTemplate");

        group.MapPatch("/{id:length(21)}", async (
            string id, UpdateTemplateRequest request, PatchNotesDbContext db) =>
        {
            var template = await db.WatchlistTemplates.FindAsync(id);
            if (template == null)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            // Only fields actually present in the request are touched, so a caller updating the
            // sort order cannot accidentally blank the description.
            if (request.Name != null)
            {
                var name = request.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Results.BadRequest(new ApiError("Name cannot be empty"));
                }

                if (await db.WatchlistTemplates.AnyAsync(t => t.Name == name && t.Id != id))
                {
                    return Results.Conflict(new ApiError($"A template named '{name}' already exists"));
                }

                template.Name = name;
            }

            if (request.Description != null)
            {
                template.Description = request.Description.Trim();
            }

            if (request.SortOrder.HasValue)
            {
                template.SortOrder = request.SortOrder.Value;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new AdminTemplateDto
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                SortOrder = template.SortOrder,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
                PackageCount = await db.WatchlistTemplatePackages.CountAsync(tp => tp.WatchlistTemplateId == id),
                Packages = [],
            });
        })
        .Produces<AdminTemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("UpdateWatchlistTemplate");

        group.MapDelete("/{id:length(21)}", async (string id, PatchNotesDbContext db) =>
        {
            var template = await db.WatchlistTemplates
                .Include(t => t.TemplatePackages)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            // Membership rows go with it. Packages themselves are untouched -- a template is a
            // curated list, not an owner, and users who already applied it keep their watchlist.
            db.WatchlistTemplatePackages.RemoveRange(template.TemplatePackages);
            db.WatchlistTemplates.Remove(template);
            await db.SaveChangesAsync();

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("DeleteWatchlistTemplate");

        // PUT /{id}/packages — replace the membership wholesale.
        //
        // Replace rather than add/remove: a curated list is edited as a list, and a single
        // idempotent call means the CLI does not have to diff current against desired state.
        group.MapPut("/{id:length(21)}/packages", async (
            string id, SetTemplatePackagesRequest request, PatchNotesDbContext db) =>
        {
            var template = await db.WatchlistTemplates
                .Include(t => t.TemplatePackages)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
            {
                return Results.NotFound(new ApiError("Template not found"));
            }

            var requested = (request.PackageIds ?? []).Distinct().ToList();

            // Every id is validated before anything changes, so a typo cannot leave the template
            // half-updated.
            var existingIds = await db.Packages
                .Where(p => requested.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();

            var missing = requested.Except(existingIds).ToList();
            if (missing.Count > 0)
            {
                return Results.BadRequest(new ApiError(
                    $"Unknown package ids: {string.Join(", ", missing)}"));
            }

            db.WatchlistTemplatePackages.RemoveRange(template.TemplatePackages);
            foreach (var packageId in requested)
            {
                db.WatchlistTemplatePackages.Add(new WatchlistTemplatePackage
                {
                    WatchlistTemplateId = id,
                    PackageId = packageId,
                });
            }

            await db.SaveChangesAsync();

            return Results.Ok(new SetTemplatePackagesResult
            {
                TemplateId = id,
                PackageCount = requested.Count,
            });
        })
        .Produces<SetTemplatePackagesResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("SetWatchlistTemplatePackages");

        return app;
    }
}

public class AdminTemplateDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int PackageCount { get; set; }
    public required List<AdminTemplatePackageDto> Packages { get; set; }
}

public class AdminTemplatePackageDto
{
    public required string PackageId { get; set; }
    public required string Name { get; set; }
    public required string GithubOwner { get; set; }
    public required string GithubRepo { get; set; }
}

public class CreateTemplateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? SortOrder { get; set; }
}

public class UpdateTemplateRequest
{
    /// <summary>Null leaves the current value alone; this is a partial update.</summary>
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? SortOrder { get; set; }
}

public class SetTemplatePackagesRequest
{
    public List<string>? PackageIds { get; set; }
}

public class SetTemplatePackagesResult
{
    public required string TemplateId { get; set; }
    public int PackageCount { get; set; }
}
