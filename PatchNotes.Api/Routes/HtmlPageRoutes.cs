using System.Text;
using System.Text.Json;
using System.Web;
using Markdig;
using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;

namespace PatchNotes.Api.Routes;

public static class HtmlPageRoutes
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static WebApplication MapHtmlPageRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/html").WithTags("HtmlPages");

        // GET /html/packages/{owner}/{repo} - Package detail HTML page
        group.MapGet("/packages/{owner}/{repo}", async (string owner, string repo, PatchNotesDbContext db) =>
        {
            var package = await db.Packages
                .AsNoTracking()
                .Where(p => p.GithubOwner == owner && p.GithubRepo == repo)
                .Select(p => new PackageDetailInfoDto
                {
                    Id = p.Id, Name = p.Name,
                    GithubOwner = p.GithubOwner, GithubRepo = p.GithubRepo, NpmName = p.NpmName,
                })
                .FirstOrDefaultAsync();

            if (package == null)
            {
                return Results.NotFound(new ApiError("Package not found"));
            }

            var groups = await db.Releases
                .AsNoTracking()
                .Where(r => r.PackageId == package.Id)
                .GroupBy(r => new { r.MajorVersion, r.IsPrerelease })
                .Select(g => new PackageDetailGroupDto
                {
                    MajorVersion = g.Key.MajorVersion,
                    IsPrerelease = g.Key.IsPrerelease,
                    VersionRange = $"v{g.Key.MajorVersion}.x",
                    ReleaseCount = g.Count(),
                    LastUpdated = g.Max(r => r.PublishedAt),
                    Releases = g.OrderByDescending(r => r.PublishedAt)
                        .Select(r => new PackageDetailReleaseDto
                        {
                            Id = r.Id, Tag = r.Tag, Title = r.Title,
                            Body = r.Body, PublishedAt = r.PublishedAt,
                        })
                        .ToList(),
                })
                .OrderByDescending(g => g.LastUpdated)
                .ToListAsync();

            var summaries = await db.ReleaseSummaries
                .AsNoTracking()
                .Where(s => s.PackageId == package.Id)
                .ToDictionaryAsync(
                    s => (s.MajorVersion, s.IsPrerelease),
                    s => s.Summary);

            foreach (var g in groups)
            {
                summaries.TryGetValue((g.MajorVersion, g.IsPrerelease), out var summary);
                g.Summary = summary;
            }

            var title = $"{owner}/{repo} | My Release Notes";
            var description = $"Track GitHub releases for {owner}/{repo}. AI-powered summaries and notifications.";
            var path = $"/packages/{owner}/{repo}";

            var bodyHtml = BuildPackageDetailHtml(owner, repo, package.NpmName, package.Id, groups);
            var jsonLd = JsonSerializer.Serialize(new
            {
                @context = "https://schema.org",
                @type = "SoftwareSourceCode",
                name = repo,
                codeRepository = $"https://github.com/{owner}/{repo}",
                author = new { @type = "Organization", name = owner },
                description = $"Release notes for {owner}/{repo}",
            });
            return Results.Content(HtmlTemplate.Wrap(title, description, path, bodyHtml, jsonLd), "text/html");
        })
        .Produces<string>(StatusCodes.Status200OK, "text/html")
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetPackageHtml")
        .ExcludeFromDescription();

        // GET /html/releases/{id} - Release detail HTML page
        group.MapGet("/releases/{id}", async (string id, PatchNotesDbContext db) =>
        {
            var release = await db.Releases
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new ReleaseDto
                {
                    Id = r.Id, Tag = r.Tag, Title = r.Title, Body = r.Body,
                    PublishedAt = r.PublishedAt, FetchedAt = r.FetchedAt,
                    Package = new ReleasePackageDto
                    {
                        Id = r.Package.Id, NpmName = r.Package.NpmName,
                        GithubOwner = r.Package.GithubOwner, GithubRepo = r.Package.GithubRepo,
                    },
                })
                .FirstOrDefaultAsync();

            if (release == null)
            {
                return Results.NotFound(new ApiError("Release not found"));
            }

            var owner = release.Package.GithubOwner;
            var repo = release.Package.GithubRepo;
            var displayTitle = release.Title ?? release.Tag;
            var title = $"{owner}/{repo} {release.Tag} | My Release Notes";
            var description = $"Release notes for {owner}/{repo} {release.Tag}.";
            var path = $"/releases/{release.Id}";

            var bodyHtml = BuildReleaseDetailHtml(release, displayTitle);
            var jsonLd = JsonSerializer.Serialize(new
            {
                @context = "https://schema.org",
                @type = "TechArticle",
                headline = $"{owner}/{repo} {release.Tag}",
                datePublished = release.PublishedAt,
                about = new
                {
                    @type = "SoftwareSourceCode",
                    name = repo,
                    version = release.Tag,
                    codeRepository = $"https://github.com/{owner}/{repo}",
                },
            });
            return Results.Content(HtmlTemplate.Wrap(title, description, path, bodyHtml, jsonLd), "text/html");
        })
        .Produces<string>(StatusCodes.Status200OK, "text/html")
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetReleaseHtml")
        .ExcludeFromDescription();

        // GET /html/emails/{id} - Sent digest email "view in browser" page
        group.MapGet("/emails/{id}", async (string id, PatchNotesDbContext db) =>
        {
            var email = await db.SentDigestEmails
                .AsNoTracking()
                .Where(e => e.Id == id && e.Status == "sent")
                .Select(e => new { e.Subject, e.HtmlBody })
                .FirstOrDefaultAsync();

            if (email == null)
            {
                return Results.NotFound(new ApiError("Email not found"));
            }

            var bodyHtml = new StringBuilder(email.HtmlBody.Length + 512);
            bodyHtml.Append(HtmlTemplate.Header(("Email", null)));
            bodyHtml.Append("<main><div class=\"container\">");
            bodyHtml.Append("<div class=\"card card-padded\">");
            bodyHtml.Append(email.HtmlBody);
            bodyHtml.Append("</div></div></main>");

            var title = $"{Encode(email.Subject)} | My Release Notes";
            return Results.Content(
                HtmlTemplate.Wrap(title, email.Subject, $"/html/emails/{id}", bodyHtml.ToString()),
                "text/html");
        })
        .AllowAnonymous()
        .Produces<string>(StatusCodes.Status200OK, "text/html")
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetEmailHtml")
        .ExcludeFromDescription();

        // GET /html/packages/{owner} - Owner package listing HTML page
        group.MapGet("/packages/{owner}", async (string owner, PatchNotesDbContext db) =>
        {
            var packages = await db.Packages
                .AsNoTracking()
                .Where(p => p.GithubOwner == owner)
                .OrderBy(p => p.Name)
                .Select(p => new OwnerPackageDto
                {
                    Id = p.Id, Name = p.Name, NpmName = p.NpmName,
                    GithubOwner = p.GithubOwner, GithubRepo = p.GithubRepo,
                    LatestVersion = p.Releases
                        .OrderByDescending(r => r.PublishedAt)
                        .Select(r => r.Tag)
                        .FirstOrDefault(),
                    LastUpdated = p.Releases
                        .Max(r => (DateTimeOffset?)r.PublishedAt),
                })
                .ToListAsync();

            if (packages.Count == 0)
            {
                return Results.NotFound(new ApiError("No packages found for this owner"));
            }

            var title = $"{owner} Packages | My Release Notes";
            var description = $"Track GitHub releases for {owner} packages. AI-powered summaries and notifications.";
            var path = $"/packages/{owner}";

            var bodyHtml = BuildOwnerListHtml(owner, packages);
            return Results.Content(HtmlTemplate.Wrap(title, description, path, bodyHtml), "text/html");
        })
        .Produces<string>(StatusCodes.Status200OK, "text/html")
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetOwnerPackagesHtml")
        .ExcludeFromDescription();

        return app;
    }

    private static string BuildPackageDetailHtml(
        string owner, string repo, string? npmName, string packageId, List<PackageDetailGroupDto> groups)
    {
        var sb = new StringBuilder(4096);
        var path = $"/packages/{owner}/{repo}";
        sb.Append(HtmlTemplate.Header(path,
            (owner, $"/html/packages/{owner}"),
            (repo, null)));

        sb.Append("<main><div class=\"container\">");
        sb.Append(HtmlTemplate.HeroCard(path));

        if (groups.Count > 0)
        {
            sb.Append("<div class=\"space-y-4\">");
            foreach (var g in groups)
            {
                var displayName = npmName ?? $"{owner}/{repo}";

                sb.Append("<div class=\"card\">");
                sb.Append("<div class=\"group-header\">");
                sb.Append("<div class=\"flex items-center gap-3\">");
                sb.Append($"<span class=\"group-title\">{Encode(displayName)}</span>");
                sb.Append($"<span class=\"badge badge-patch\">{Encode(g.VersionRange)}</span>");
                if (g.IsPrerelease)
                    sb.Append("<span class=\"badge badge-prerelease\">prerelease</span>");
                sb.Append("</div>");
                sb.Append($"<span class=\"group-meta\">{g.ReleaseCount} release{(g.ReleaseCount != 1 ? "s" : "")}</span>");
                var shareUrl = $"https://www.myreleasenotes.ai/s/{Encode(packageId)}";
                sb.Append($"<button class=\"copy-link-btn\" onclick=\"navigator.clipboard.writeText('{shareUrl}');this.textContent='Copied!';setTimeout(()=>this.innerHTML='<svg width=&quot;14&quot; height=&quot;14&quot; viewBox=&quot;0 0 24 24&quot; fill=&quot;none&quot; stroke=&quot;currentColor&quot; stroke-width=&quot;2&quot; stroke-linecap=&quot;round&quot; stroke-linejoin=&quot;round&quot;><path d=&quot;M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8&quot;/><polyline points=&quot;16 6 12 2 8 6&quot;/><line x1=&quot;12&quot; x2=&quot;12&quot; y1=&quot;2&quot; y2=&quot;15&quot;/></svg> Share',1500)\"><svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8\"/><polyline points=\"16 6 12 2 8 6\"/><line x1=\"12\" x2=\"12\" y1=\"2\" y2=\"15\"/></svg> Share</button>");
                sb.Append("</div>");

                if (!string.IsNullOrEmpty(g.Summary))
                {
                    var summaryHtml = Markdown.ToHtml(g.Summary, MarkdownPipeline);
                    sb.Append($"<div class=\"group-summary prose-release\">{summaryHtml}</div>");
                }

                sb.Append("<div class=\"release-list\">");
                foreach (var r in g.Releases)
                {
                    sb.Append("<div class=\"release-item\">");
                    sb.Append("<div class=\"flex items-center gap-2\">");
                    sb.Append(HtmlTemplate.VersionBadge(r.Tag));
                    sb.Append($"<a href=\"/html/releases/{Encode(r.Id)}\">{Encode(r.Title ?? r.Tag)}</a>");
                    sb.Append("</div>");
                    sb.Append($"<span class=\"text-xs text-tertiary\">{r.PublishedAt:MMM d, yyyy}</span>");
                    sb.Append("</div>");
                }
                sb.Append("</div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }
        else
        {
            sb.Append("<p class=\"text-secondary\" style=\"text-align:center;padding:2rem 0\">No releases found for this package.</p>");
        }

        sb.Append("</div></main>");
        return sb.ToString();
    }

    private static string BuildReleaseDetailHtml(ReleaseDto release, string displayTitle)
    {
        var sb = new StringBuilder(4096);
        var owner = release.Package.GithubOwner;
        var repo = release.Package.GithubRepo;

        sb.Append(HtmlTemplate.Header($"/releases/{release.Id}",
            (owner, $"/html/packages/{owner}"),
            (repo, $"/html/packages/{owner}/{repo}"),
            (release.Tag, null)));

        sb.Append("<main><div class=\"container\">");

        sb.Append("<div class=\"card card-padded mb-8\">");
        sb.Append("<div class=\"flex items-center gap-3 mb-4\">");
        sb.Append(HtmlTemplate.VersionBadge(release.Tag));
        sb.Append("<div>");
        sb.Append($"<div class=\"font-semibold text-lg\">{Encode(displayTitle)}</div>");
        if (release.Package.NpmName != null)
            sb.Append($"<div class=\"text-sm text-tertiary mt-1\">{Encode(release.Package.NpmName)}</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"flex flex-wrap items-center gap-6 text-sm\">");
        var githubUrl = $"https://github.com/{owner}/{repo}/releases/tag/{release.Tag}";
        sb.Append($"<a href=\"{Encode(githubUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">View on GitHub</a>");
        sb.Append($"<a href=\"/html/packages/{Encode(owner)}/{Encode(repo)}\">View Package</a>");
        sb.Append($"<span class=\"text-tertiary\">Published: {release.PublishedAt:MMM d, yyyy}</span>");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("<h2 class=\"text-xl font-semibold mb-4\">Release Notes</h2>");
        sb.Append("<div class=\"card card-padded\">");
        if (!string.IsNullOrEmpty(release.Body))
        {
            var bodyHtml = Markdown.ToHtml(release.Body, MarkdownPipeline);
            sb.Append($"<div class=\"prose-release\">{bodyHtml}</div>");
        }
        else
        {
            sb.Append("<p class=\"text-tertiary\" style=\"font-style:italic\">No release notes available.</p>");
        }
        sb.Append("</div>");

        sb.Append("</div></main>");
        return sb.ToString();
    }

    private static string BuildOwnerListHtml(string owner, List<OwnerPackageDto> packages)
    {
        var sb = new StringBuilder(2048);
        sb.Append(HtmlTemplate.Header($"/packages/{owner}", (owner, null)));

        sb.Append("<main><div class=\"container\">");
        sb.Append($"<h1 class=\"text-xl font-semibold mb-4\">{Encode(owner)} Packages</h1>");
        sb.Append("<div class=\"card\">");

        foreach (var pkg in packages)
        {
            sb.Append("<div class=\"pkg-list-item\">");
            sb.Append("<div>");
            sb.Append($"<a href=\"/html/packages/{Encode(owner)}/{Encode(pkg.GithubRepo)}\" class=\"font-semibold\">{Encode(pkg.Name)}</a>");
            if (pkg.LatestVersion != null)
                sb.Append($"<span class=\"text-sm text-secondary\" style=\"margin-left:0.5rem\">{Encode(pkg.LatestVersion)}</span>");
            sb.Append("</div>");
            if (pkg.LastUpdated.HasValue)
                sb.Append($"<span class=\"text-xs text-tertiary\">{pkg.LastUpdated.Value:MMM d, yyyy}</span>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        sb.Append("</div></main>");
        return sb.ToString();
    }

    private static string Encode(string value) => HttpUtility.HtmlEncode(value);
}
