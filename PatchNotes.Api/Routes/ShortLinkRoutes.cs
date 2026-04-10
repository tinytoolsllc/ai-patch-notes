using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

public static class ShortLinkRoutes
{
    private const string SiteBaseUrl = "https://www.myreleasenotes.ai";

    public static WebApplication MapShortLinkRoutes(this WebApplication app)
    {
        // GET /s/{id} — resolve a package or release ID and redirect to its SPA page
        app.MapGet("/s/{id}", async (string id, HttpContext httpContext, PatchNotesDbContext db) =>
        {
            // Check releases first (most common share target)
            var release = await db.Releases
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new { r.Id })
                .FirstOrDefaultAsync();

            if (release != null)
            {
                httpContext.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Redirect($"{SiteBaseUrl}/releases/{release.Id}", permanent: true);
            }

            // Then check packages
            var package = await db.Packages
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.GithubOwner, p.GithubRepo })
                .FirstOrDefaultAsync();

            if (package != null)
            {
                httpContext.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Redirect($"{SiteBaseUrl}/packages/{package.GithubOwner}/{package.GithubRepo}", permanent: true);
            }

            return Results.NotFound();
        })
        .WithTags("ShortLinks")
        .Produces(StatusCodes.Status301MovedPermanently)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("ResolveShortLink")
        .ExcludeFromDescription();

        return app;
    }
}
