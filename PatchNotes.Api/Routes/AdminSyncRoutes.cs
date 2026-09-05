using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

/// <summary>
/// Bulk sync operations.
/// </summary>
/// <remarks>
/// /api/admin/packages holds operational actions on packages -- health, reset-sync, disable-sync,
/// trigger-sync. Package CRUD lives on /api/packages, where GET is public and the mutations are
/// admin-gated. Creating a package is CRUD, so it lives there too rather than here.
/// </remarks>
public static class AdminSyncRoutes
{
    public static WebApplication MapAdminSyncRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();
        var requireAdmin = RouteUtils.CreateAdminFilter();

        var sync = app.MapGroup("/api/admin/sync")
            .WithTags("AdminSync")
            .AddEndpointFilterFactory(requireAuth)
            .AddEndpointFilterFactory(requireAdmin);

        // POST /api/admin/sync/trigger-all — queue every enabled package for the next sync run.
        //
        // This is not the per-package trigger applied in a loop. That one also clears failure
        // counters and re-enables sync, which is the right behaviour when an operator is fixing one
        // broken package and the wrong behaviour in bulk: it would silently resurrect every package
        // someone had deliberately disabled.
        //
        // So this only nudges. Packages with sync disabled are left alone, and failure state is
        // preserved -- use reset-sync on a specific package to clear that.
        sync.MapPost("/trigger-all", async (
            PatchNotesDbContext db,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            IHostApplicationLifetime appLifetime) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.AdminSyncRoutes");

            var syncUrl = configuration["SyncFunction:Url"];
            if (string.IsNullOrEmpty(syncUrl))
            {
                return Results.Json(
                    new ApiError("Sync function URL not configured"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var enabled = await db.Packages.Where(p => !p.IsSyncDisabled).ToListAsync();
            foreach (var package in enabled)
            {
                package.LastFetchedAt = null;
            }
            await db.SaveChangesAsync();

            var skipped = await db.Packages.CountAsync(p => p.IsSyncDisabled);

            var syncKey = configuration["SyncFunction:Key"];
            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = httpClientFactory.CreateClient();
                    using var request = new HttpRequestMessage(HttpMethod.Post, syncUrl);
                    if (!string.IsNullOrEmpty(syncKey))
                    {
                        request.Headers.Add("x-functions-key", syncKey);
                    }
                    request.Content = new StringContent("");
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    var response = await http.SendAsync(request, appLifetime.ApplicationStopping);
                    logger.LogInformation(
                        "Trigger-all sync returned {StatusCode} for {Count} packages",
                        (int)response.StatusCode, enabled.Count);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Trigger-all sync request failed");
                }
            });

            return Results.Ok(new TriggerAllResult
            {
                PackagesQueued = enabled.Count,
                PackagesSkipped = skipped,
            });
        })
        .Produces<TriggerAllResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithName("TriggerAllSync");

        return app;
    }
}

public class TriggerAllResult
{
    /// <summary>Packages whose LastFetchedAt was cleared, so the next run re-fetches them.</summary>
    public int PackagesQueued { get; set; }

    /// <summary>Packages left alone because sync is disabled for them.</summary>
    public int PackagesSkipped { get; set; }
}
