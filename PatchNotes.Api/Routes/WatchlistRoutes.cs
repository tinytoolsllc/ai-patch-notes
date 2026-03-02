using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PatchNotes.Data;
using PatchNotes.Api.Stytch;

namespace PatchNotes.Api.Routes;

public static class WatchlistRoutes
{
    internal const int FreeWatchlistLimit = 5;
    private const int MaxWatchlistSize = 1000;

    public static WebApplication MapWatchlistRoutes(this WebApplication app)
    {
        var requireAuth = RouteUtils.CreateAuthFilter();

        var group = app.MapGroup("/api/watchlist").WithTags("Watchlist");

        // GET /api/watchlist/templates — return available watchlist templates (public)
        group.MapGet("/templates", async (IOptions<WatchlistTemplateOptions> options, PatchNotesDbContext db) =>
        {
            // Collect all unique owner/repo pairs across templates
            var allPairs = options.Value.Templates
                .SelectMany(t => t.Packages)
                .Where(p => p.Contains('/'))
                .Distinct()
                .Select(p => { var parts = p.Split('/'); return new { Owner = parts[0], Repo = parts[1], Key = p }; })
                .ToList();

            // Single DB query to resolve all packages
            var owners = allPairs.Select(p => p.Owner).Distinct().ToArray();
            var repos = allPairs.Select(p => p.Repo).Distinct().ToArray();
            var packages = await db.Packages
                .AsNoTracking()
                .Where(p => owners.Contains(p.GithubOwner) && repos.Contains(p.GithubRepo))
                .Select(p => new { p.Id, p.GithubOwner, p.GithubRepo })
                .ToListAsync();

            var lookup = packages.ToDictionary(p => $"{p.GithubOwner}/{p.GithubRepo}", p => p.Id);

            var templates = options.Value.Templates.Select(t => new WatchlistTemplateDto
            {
                Name = t.Name,
                Description = t.Description,
                Packages = t.Packages,
                PackageIds = t.Packages
                    .Where(p => lookup.ContainsKey(p))
                    .Select(p => lookup[p])
                    .ToArray()
            }).ToArray();

            return Results.Ok(templates);
        })
        .Produces<WatchlistTemplateDto[]>(StatusCodes.Status200OK)
        .WithName("GetWatchlistTemplates");

        // GET /api/watchlist — return list of package IDs the current user is watching
        group.MapGet("/", async (HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.Ok(Array.Empty<WatchlistPackageDto>());
            }

            var packages = await db.Watchlists
                .AsNoTracking()
                .Where(w => w.UserId == user.Id)
                .Select(w => new WatchlistPackageDto(
                    w.Package.Id,
                    w.Package.Name,
                    w.Package.GithubOwner,
                    w.Package.GithubRepo,
                    w.Package.NpmName
                ))
                .ToArrayAsync();

            return Results.Ok(packages);
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<WatchlistPackageDto[]>(StatusCodes.Status200OK)
        .WithName("GetWatchlist");

        // PUT /api/watchlist — replace the entire watchlist (bulk set)
        group.MapPut("/", async (SetWatchlistRequest request, HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var packageIds = request.PackageIds ?? [];
            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isPro = user.IsPro || (session?.IsAdmin ?? false);

            if (!isPro && packageIds.Length > FreeWatchlistLimit)
            {
                return Results.Json(new ApiError($"Free plan is limited to {FreeWatchlistLimit} packages. Upgrade to Pro for unlimited."), statusCode: 403);
            }
            if (packageIds.Length > MaxWatchlistSize)
            {
                return Results.BadRequest(new ApiError($"Watchlist cannot exceed {MaxWatchlistSize} packages"));
            }

            var distinctIds = packageIds.Distinct().ToArray();
            var existingPackageCount = await db.Packages
                .Where(p => distinctIds.Contains(p.Id))
                .CountAsync();
            if (existingPackageCount != distinctIds.Length)
            {
                return Results.BadRequest(new ApiError("One or more package IDs do not exist"));
            }

            var strategy = db.Database.CreateExecutionStrategy();
            var resultIds = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();

                var existing = await db.Watchlists
                    .Where(w => w.UserId == user.Id)
                    .ToListAsync();
                db.Watchlists.RemoveRange(existing);

                foreach (var packageId in distinctIds)
                {
                    db.Watchlists.Add(new Watchlist
                    {
                        UserId = user.Id,
                        PackageId = packageId,
                    });
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return await db.Watchlists
                    .Where(w => w.UserId == user.Id)
                    .Select(w => w.PackageId)
                    .ToArrayAsync();
            });

            return Results.Ok(resultIds);
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<string[]>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("SetWatchlist");

        // POST /api/watchlist/from-template — apply a watchlist template in one request
        group.MapPost("/from-template", async (ApplyWatchlistTemplateRequest request, HttpContext httpContext, PatchNotesDbContext db, IOptions<WatchlistTemplateOptions> options, IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IHostApplicationLifetime appLifetime) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.WatchlistRoutes");
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var template = options.Value.Templates.FirstOrDefault(t =>
                string.Equals(t.Name, request.TemplateName, StringComparison.OrdinalIgnoreCase));
            if (template == null)
            {
                return Results.BadRequest(new ApiError("Template not found"));
            }

            var ownerRepoPairs = template.Packages
                .Where(p => p.Contains('/'))
                .Select(p => { var parts = p.Split('/'); return new { Owner = parts[0], Repo = parts[1], Key = p }; })
                .ToList();

            if (ownerRepoPairs.Count == 0)
            {
                return Results.Ok(new ApplyWatchlistTemplateResponse { PackageIds = [] });
            }

            // Find existing packages in one query
            var ownerList = ownerRepoPairs.Select(p => p.Owner).Distinct().ToArray();
            var repoList = ownerRepoPairs.Select(p => p.Repo).Distinct().ToArray();
            var existingPackages = await db.Packages
                .Where(p => ownerList.Contains(p.GithubOwner) && repoList.Contains(p.GithubRepo))
                .ToListAsync();

            var existingLookup = existingPackages.ToDictionary(p => $"{p.GithubOwner}/{p.GithubRepo}");

            // Create missing packages
            var newPackages = new List<Package>();
            foreach (var pair in ownerRepoPairs)
            {
                if (!existingLookup.ContainsKey(pair.Key))
                {
                    var pkg = new Package
                    {
                        Name = pair.Repo,
                        Url = $"https://github.com/{pair.Owner}/{pair.Repo}",
                        GithubOwner = pair.Owner,
                        GithubRepo = pair.Repo,
                    };
                    db.Packages.Add(pkg);
                    newPackages.Add(pkg);
                    existingLookup[pair.Key] = pkg;
                }
            }

            if (newPackages.Count > 0)
            {
                await db.SaveChangesAsync();
            }

            // Add all to watchlist, skipping already-watched
            var allPackageIds = ownerRepoPairs.Select(p => existingLookup[p.Key].Id).Distinct().ToList();
            var alreadyWatched = await db.Watchlists
                .Where(w => w.UserId == user.Id && allPackageIds.Contains(w.PackageId))
                .Select(w => w.PackageId)
                .ToListAsync();

            var toAdd = allPackageIds.Except(alreadyWatched).ToList();
            foreach (var packageId in toAdd)
            {
                db.Watchlists.Add(new Watchlist
                {
                    UserId = user.Id,
                    PackageId = packageId,
                });
            }

            if (toAdd.Count > 0)
            {
                await db.SaveChangesAsync();
            }

            // Fire-and-forget: ping sync for new packages
            if (newPackages.Count > 0)
            {
                var syncUrl = configuration["SyncFunction:Url"];
                var syncKey = configuration["SyncFunction:Key"];
                if (!string.IsNullOrEmpty(syncUrl))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var http = httpClientFactory.CreateClient();
                            using var syncRequest = new HttpRequestMessage(HttpMethod.Post, syncUrl);
                            if (!string.IsNullOrEmpty(syncKey))
                                syncRequest.Headers.Add("x-functions-key", syncKey);
                            syncRequest.Content = new StringContent("");
                            syncRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                            await http.SendAsync(syncRequest, appLifetime.ApplicationStopping);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to ping SyncNewPackages function");
                        }
                    }, appLifetime.ApplicationStopping);
                }
            }

            return Results.Ok(new ApplyWatchlistTemplateResponse { PackageIds = [.. allPackageIds] });
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<ApplyWatchlistTemplateResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("ApplyWatchlistTemplate");

        // POST /api/watchlist/{packageId} — add a single package to watchlist
        group.MapPost("/{packageId}", async (string packageId, HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var packageExists = await db.Packages.AnyAsync(p => p.Id == packageId);
            if (!packageExists)
            {
                return Results.NotFound(new ApiError("Package not found"));
            }

            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isPro = user.IsPro || (session?.IsAdmin ?? false);

            var watchlistSize = await db.Watchlists.CountAsync(w => w.UserId == user.Id);
            if (!isPro && watchlistSize >= FreeWatchlistLimit)
            {
                return Results.Json(new ApiError($"Free plan is limited to {FreeWatchlistLimit} packages. Upgrade to Pro for unlimited."), statusCode: 403);
            }
            if (watchlistSize >= MaxWatchlistSize)
            {
                return Results.BadRequest(new ApiError($"Watchlist cannot exceed {MaxWatchlistSize} packages"));
            }

            var alreadyWatching = await db.Watchlists
                .AnyAsync(w => w.UserId == user.Id && w.PackageId == packageId);
            if (alreadyWatching)
            {
                return Results.Conflict(new ApiError("Already watching this package"));
            }

            db.Watchlists.Add(new Watchlist
            {
                UserId = user.Id,
                PackageId = packageId,
            });
            await db.SaveChangesAsync();

            return Results.Created($"/api/watchlist/{packageId}", packageId);
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<string>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithName("AddToWatchlist");

        // POST /api/watchlist/github/{owner}/{repo} — add a GitHub repo to watchlist, creating package if needed
        group.MapPost("/github/{owner}/{repo}", async (string owner, string repo, HttpContext httpContext, PatchNotesDbContext db, IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IHostApplicationLifetime appLifetime) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Routes.WatchlistRoutes");
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NotFound(new ApiError("User not found"));
            }

            var session = httpContext.Items["StytchSession"] as StytchSessionResult;
            var isPro = user.IsPro || (session?.IsAdmin ?? false);

            var watchlistSize = await db.Watchlists.CountAsync(w => w.UserId == user.Id);
            if (!isPro && watchlistSize >= FreeWatchlistLimit)
            {
                return Results.Json(new ApiError($"Free plan is limited to {FreeWatchlistLimit} packages. Upgrade to Pro for unlimited."), statusCode: 403);
            }
            if (watchlistSize >= MaxWatchlistSize)
            {
                return Results.BadRequest(new ApiError($"Watchlist cannot exceed {MaxWatchlistSize} packages"));
            }

            // Find or create the package
            var package = await db.Packages
                .FirstOrDefaultAsync(p => p.GithubOwner == owner && p.GithubRepo == repo);

            var isNewPackage = package == null;
            if (isNewPackage)
            {
                package = new Package
                {
                    Name = repo,
                    Url = $"https://github.com/{owner}/{repo}",
                    GithubOwner = owner,
                    GithubRepo = repo,
                };
                db.Packages.Add(package);
                await db.SaveChangesAsync();
            }

            var alreadyWatching = await db.Watchlists
                .AnyAsync(w => w.UserId == user.Id && w.PackageId == package!.Id);
            if (alreadyWatching)
            {
                return Results.Conflict(new ApiError("Already watching this package"));
            }

            db.Watchlists.Add(new Watchlist
            {
                UserId = user.Id,
                PackageId = package!.Id,
            });
            await db.SaveChangesAsync();

            // Fire-and-forget: ping the Function to sync new packages
            if (isNewPackage)
            {
                var syncUrl = configuration["SyncFunction:Url"];
                var syncKey = configuration["SyncFunction:Key"];
                if (!string.IsNullOrEmpty(syncUrl))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var http = httpClientFactory.CreateClient();
                            using var request = new HttpRequestMessage(HttpMethod.Post, syncUrl);
                            if (!string.IsNullOrEmpty(syncKey))
                                request.Headers.Add("x-functions-key", syncKey);
                            request.Content = new StringContent("");
                            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                            await http.SendAsync(request, appLifetime.ApplicationStopping);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to ping SyncNewPackages function");
                        }
                    }, appLifetime.ApplicationStopping);
                }
            }

            return Results.Created($"/api/watchlist/{package.Id}", new AddFromGitHubResponse { PackageId = package.Id });
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces<AddFromGitHubResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithName("AddToWatchlistFromGitHub");

        // DELETE /api/watchlist/{packageId} — remove a single package from watchlist
        group.MapDelete("/{packageId}", async (string packageId, HttpContext httpContext, PatchNotesDbContext db) =>
        {
            var stytchUserId = httpContext.Items["StytchUserId"] as string;
            if (stytchUserId == null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
            if (user == null)
            {
                return Results.NoContent();
            }

            var entry = await db.Watchlists
                .FirstOrDefaultAsync(w => w.UserId == user.Id && w.PackageId == packageId);
            if (entry != null)
            {
                db.Watchlists.Remove(entry);
                await db.SaveChangesAsync();
            }

            return Results.NoContent();
        })
        .AddEndpointFilterFactory(requireAuth)
        .Produces(StatusCodes.Status204NoContent)
        .WithName("RemoveFromWatchlist");

        return app;
    }
}

public record WatchlistPackageDto(
    string Id,
    string Name,
    string GithubOwner,
    string GithubRepo,
    string? NpmName
);

public record SetWatchlistRequest(string[] PackageIds);

public class WatchlistTemplateDto
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string[] Packages { get; set; } = [];
    public string[] PackageIds { get; set; } = [];
}

public class AddFromGitHubResponse
{
    public required string PackageId { get; set; }
}

public class ApplyWatchlistTemplateRequest
{
    public required string TemplateName { get; set; }
}

public class ApplyWatchlistTemplateResponse
{
    public string[] PackageIds { get; set; } = [];
}
