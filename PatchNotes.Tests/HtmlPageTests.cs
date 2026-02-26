using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class HtmlPageTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    #region GET /html/packages/{owner}/{repo}

    [Fact]
    public async Task GetPackageHtml_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _client.GetAsync("/html/packages/nonexistent/repo");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPackageHtml_ReturnsHtml_WithContent()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "React", Url = "https://github.com/facebook/react", NpmName = "react", GithubOwner = "facebook", GithubRepo = "react" };
        db.Packages.Add(pkg);
        db.Releases.Add(new Release
        {
            PackageId = pkg.Id, Tag = "v19.0.0", Title = "React 19", Body = "# Major Release\n\nNew features.",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 19,
        });
        db.ReleaseSummaries.Add(new ReleaseSummary
        {
            PackageId = pkg.Id, MajorVersion = 19, IsPrerelease = false,
            Summary = "React 19 introduces concurrent features.", GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/html/packages/facebook/react");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("facebook");
        html.Should().Contain("react");
        html.Should().Contain("v19.0.0");
        html.Should().Contain("React 19 introduces concurrent features.");
    }

    [Fact]
    public async Task GetPackageHtml_ContainsSeoMetaTags()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package { Name = "Vue", Url = "https://github.com/vuejs/core", NpmName = "vue", GithubOwner = "vuejs", GithubRepo = "core" });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/html/packages/vuejs/core");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("<meta property=\"og:title\"");
        html.Should().Contain("<meta name=\"description\"");
        html.Should().Contain("<link rel=\"canonical\"");
        html.Should().Contain("application/ld+json");
    }

    [Fact]
    public async Task GetPackageHtml_ContainsThemeScript()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package { Name = "Lodash", Url = "https://github.com/lodash/lodash", NpmName = "lodash", GithubOwner = "lodash", GithubRepo = "lodash" });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/html/packages/lodash/lodash");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("patchnotes-theme");
        html.Should().Contain("prefers-color-scheme");
    }

    [Fact]
    public async Task GetPackageHtml_ContainsHeroCard()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package { Name = "Express", Url = "https://github.com/expressjs/express", NpmName = "express", GithubOwner = "expressjs", GithubRepo = "express" });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/html/packages/expressjs/express");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("data-nosnippet");
        html.Should().Contain("Never miss a release");
        html.Should().Contain("Get Started Free");
    }

    #endregion

    #region GET /html/releases/{id}

    [Fact]
    public async Task GetReleaseHtml_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        var response = await _client.GetAsync("/html/releases/nonexistent-id-12345");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReleaseHtml_ReturnsHtml_WithRenderedMarkdown()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "Next.js", Url = "https://github.com/vercel/next.js", GithubOwner = "vercel", GithubRepo = "next.js" };
        db.Packages.Add(pkg);
        var release = new Release
        {
            PackageId = pkg.Id, Tag = "v15.0.0", Title = "Next.js 15",
            Body = "## Breaking Changes\n\n- Removed legacy API routes\n- **Bold text** and `inline code`",
            PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 15,
        };
        db.Releases.Add(release);
        await db.SaveChangesAsync();
        var releaseId = release.Id;

        // Act
        var response = await _client.GetAsync($"/html/releases/{releaseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Next.js 15");
        html.Should().Contain("vercel");
        html.Should().Contain("v15.0.0");
        // Markdown should be rendered to HTML
        html.Should().Contain("<h2");
        html.Should().Contain("Breaking Changes");
        html.Should().Contain("<strong>Bold text</strong>");
        html.Should().Contain("<code>inline code</code>");
    }

    [Fact]
    public async Task GetReleaseHtml_ContainsSeoMetaTags()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "Angular", Url = "https://github.com/angular/angular", GithubOwner = "angular", GithubRepo = "angular" };
        db.Packages.Add(pkg);
        var release = new Release
        {
            PackageId = pkg.Id, Tag = "v18.0.0", Title = "Angular 18",
            PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 18,
        };
        db.Releases.Add(release);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/html/releases/{release.Id}");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("<meta property=\"og:title\"");
        html.Should().Contain("application/ld+json");
        html.Should().Contain("TechArticle");
    }

    [Fact]
    public async Task GetReleaseHtml_WithNoBody_ShowsNoNotesMessage()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "Empty", Url = "https://github.com/test/empty", GithubOwner = "test", GithubRepo = "empty" };
        db.Packages.Add(pkg);
        var release = new Release
        {
            PackageId = pkg.Id, Tag = "v1.0.0", Body = null,
            PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 1,
        };
        db.Releases.Add(release);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/html/releases/{release.Id}");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        html.Should().Contain("No release notes available");
    }

    #endregion

    #region GET /html/packages/{owner}

    [Fact]
    public async Task GetOwnerHtml_ReturnsNotFound_WhenNoPackagesForOwner()
    {
        var response = await _client.GetAsync("/html/packages/nonexistent-owner");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOwnerHtml_ReturnsHtml_WithPackageList()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg1 = new Package { Name = "Runtime", Url = "https://github.com/dotnet/runtime", GithubOwner = "dotnet", GithubRepo = "runtime" };
        var pkg2 = new Package { Name = "EF Core", Url = "https://github.com/dotnet/efcore", GithubOwner = "dotnet", GithubRepo = "efcore" };
        db.Packages.AddRange(pkg1, pkg2);
        db.Releases.Add(new Release
        {
            PackageId = pkg1.Id, Tag = "v9.0.0",
            PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 9,
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/html/packages/dotnet");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("dotnet");
        html.Should().Contain("Runtime");
        html.Should().Contain("EF Core");
        html.Should().Contain("v9.0.0");
    }

    #endregion
}
