using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class ReleasesApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        // Clear default watchlist so these tests aren't affected by watchlist filtering
        _fixture.ConfigureServices(services =>
        {
            services.Configure<DefaultWatchlistOptions>(o => o.Packages = []);
        });
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task GetReleases_ReturnsEmptyList_WhenNoReleases()
    {
        var response = await _client.GetAsync("/api/releases");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetReleases_GivenNoDaysParam_ReturnsReleasesWithinDefaultWindow()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package = new Package { Name = "test-pkg", Url = "https://github.com/owner/repo", NpmName = "test-pkg", GithubOwner = "owner", GithubRepo = "repo" };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        // Add release within 7 days (default)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Title = "Release 1",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-3),
            FetchedAt = DateTimeOffset.UtcNow
        });
        // Add release outside 7 days
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v0.9.0",
            Title = "Old Release",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-10),
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/releases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_GivenDaysParam_FiltersToThatWindow()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package = new Package { Name = "test-pkg", Url = "https://github.com/owner/repo", NpmName = "test-pkg", GithubOwner = "owner", GithubRepo = "repo" };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v0.9.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-10), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v0.8.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-25), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/releases?days=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task GetReleases_GivenPackageIdsParam_FiltersToThosePackages()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner/repo1", NpmName = "pkg1", GithubOwner = "owner", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner/repo2", NpmName = "pkg2", GithubOwner = "owner", GithubRepo = "repo2" };
        db.Packages.AddRange(package1, package2);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = package1.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package2.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/releases?packages={package1.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_GivenMultiplePackageIds_ReturnsReleasesForAll()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner/repo1", NpmName = "pkg1", GithubOwner = "owner", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner/repo2", NpmName = "pkg2", GithubOwner = "owner", GithubRepo = "repo2" };
        var package3 = new Package { Name = "pkg3", Url = "https://github.com/owner/repo3", NpmName = "pkg3", GithubOwner = "owner", GithubRepo = "repo3" };
        db.Packages.AddRange(package1, package2, package3);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = package1.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package2.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package3.Id, Tag = "v3.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/releases?packages={package1.Id},{package2.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetReleases_GivenMultipleReleases_ReturnsNewestFirst()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package = new Package { Name = "test-pkg", Url = "https://github.com/owner/repo", NpmName = "test-pkg", GithubOwner = "owner", GithubRepo = "repo" };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-5), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v1.5.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/releases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(3);
        releases[0].GetProperty("tag").GetString().Should().Be("v2.0.0");
        releases[1].GetProperty("tag").GetString().Should().Be("v1.5.0");
        releases[2].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_GivenReleasesWithPackages_IncludesPackageInfo()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package = new Package { Name = "my-package", Url = "https://github.com/my-owner/my-repo", NpmName = "my-package", GithubOwner = "my-owner", GithubRepo = "my-repo" };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Title = "First Release",
            Body = "Release notes here",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/releases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        var release = releases[0];
        release.GetProperty("tag").GetString().Should().Be("v1.0.0");
        release.GetProperty("title").GetString().Should().Be("First Release");
        release.GetProperty("body").GetString().Should().Be("Release notes here");

        var pkg = release.GetProperty("package");
        pkg.GetProperty("npmName").GetString().Should().Be("my-package");
        pkg.GetProperty("githubOwner").GetString().Should().Be("my-owner");
        pkg.GetProperty("githubRepo").GetString().Should().Be("my-repo");
    }
}

public class ReleasesWatchlistFilterTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;
    private HttpClient _authClient = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        // Configure default watchlist to known values for testing
        _fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("DefaultWatchlist:Packages:0", "default-owner/default-repo");
        });
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
        _authClient = _fixture.CreateAuthenticatedClient();

        // Seed user for auth tests
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Users.Add(new User
        {
            StytchUserId = PatchNotesApiFixture.TestUserId,
            Email = "test@example.com",
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _authClient.Dispose();
        _client.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task GetReleases_Anonymous_NoPackagesParam_FiltersToDefaultWatchlist()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var defaultPkg = new Package { Name = "default-pkg", Url = "https://github.com/default-owner/default-repo", GithubOwner = "default-owner", GithubRepo = "default-repo" };
        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.AddRange(defaultPkg, otherPkg);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = defaultPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = otherPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/releases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_Anonymous_WithPackagesParam_UsesExplicitFilter()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var defaultPkg = new Package { Name = "default-pkg", Url = "https://github.com/default-owner/default-repo", GithubOwner = "default-owner", GithubRepo = "default-repo" };
        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.AddRange(defaultPkg, otherPkg);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = defaultPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = otherPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act - explicit packages param overrides default watchlist
        var response = await _client.GetAsync($"/api/releases?packages={otherPkg.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v2.0.0");
    }

    [Fact]
    public async Task GetReleases_Authenticated_WithWatchlist_FiltersToUserWatchlist()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var user = await db.Users.FirstAsync(u => u.StytchUserId == PatchNotesApiFixture.TestUserId);
        var watchedPkg = new Package { Name = "watched-pkg", Url = "https://github.com/watched/repo", GithubOwner = "watched", GithubRepo = "repo" };
        var defaultPkg = new Package { Name = "default-pkg", Url = "https://github.com/default-owner/default-repo", GithubOwner = "default-owner", GithubRepo = "default-repo" };
        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.AddRange(watchedPkg, defaultPkg, otherPkg);
        await db.SaveChangesAsync();

        db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = watchedPkg.Id });
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = watchedPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = defaultPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = otherPkg.Id, Tag = "v3.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/releases");

        // Assert - should only return watched package releases, not default or other
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_Authenticated_EmptyWatchlist_FallsBackToDefault()
    {
        // Arrange - user has no watchlist entries
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var defaultPkg = new Package { Name = "default-pkg", Url = "https://github.com/default-owner/default-repo", GithubOwner = "default-owner", GithubRepo = "default-repo" };
        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.AddRange(defaultPkg, otherPkg);
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = defaultPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = otherPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act - authenticated but empty watchlist → falls back to default
        var response = await _authClient.GetAsync("/api/releases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_DefaultWatchlistPackageNotInDb_ReturnsEmpty()
    {
        // Arrange - default watchlist package not seeded in DB
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.Add(otherPkg);
        await db.SaveChangesAsync();

        db.Releases.Add(new Release { PackageId = otherPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Act - default watchlist resolves to empty (no matching packages in DB)
        var response = await _client.GetAsync("/api/releases");

        // Assert - empty default watchlist means empty results, not all releases
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetReleases_Authenticated_WithPackagesParam_OverridesWatchlist()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var user = await db.Users.FirstAsync(u => u.StytchUserId == PatchNotesApiFixture.TestUserId);
        var watchedPkg = new Package { Name = "watched-pkg", Url = "https://github.com/watched/repo", GithubOwner = "watched", GithubRepo = "repo" };
        var explicitPkg = new Package { Name = "explicit-pkg", Url = "https://github.com/explicit/repo", GithubOwner = "explicit", GithubRepo = "repo" };
        db.Packages.AddRange(watchedPkg, explicitPkg);
        await db.SaveChangesAsync();

        db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = watchedPkg.Id });
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = watchedPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = explicitPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act - explicit packages param should override user's watchlist
        var response = await _authClient.GetAsync($"/api/releases?packages={explicitPkg.Id}");

        // Assert - only the explicit package's releases, not the user's watchlist
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v2.0.0");
    }

    [Fact]
    public async Task GetReleases_WatchlistTrue_Authenticated_FiltersToUserWatchlist()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var user = await db.Users.FirstAsync(u => u.StytchUserId == PatchNotesApiFixture.TestUserId);
        var watchedPkg = new Package { Name = "watched-pkg", Url = "https://github.com/watched/repo", GithubOwner = "watched", GithubRepo = "repo" };
        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.AddRange(watchedPkg, otherPkg);
        await db.SaveChangesAsync();

        db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = watchedPkg.Id });
        await db.SaveChangesAsync();

        db.Releases.AddRange(
            new Release { PackageId = watchedPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = otherPkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/releases?watchlist=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(1);
        releases[0].GetProperty("tag").GetString().Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleases_WatchlistTrue_Unauthenticated_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/releases?watchlist=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetReleases_WatchlistTrue_EmptyWatchlist_ReturnsEmpty()
    {
        // Arrange - user exists but has no watchlist entries
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var otherPkg = new Package { Name = "other-pkg", Url = "https://github.com/other/repo", GithubOwner = "other", GithubRepo = "repo" };
        db.Packages.Add(otherPkg);
        await db.SaveChangesAsync();

        db.Releases.Add(new Release { PackageId = otherPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/releases?watchlist=true");

        // Assert - authenticated but empty watchlist returns empty, not all
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetReleases_WatchlistTrueAndPackages_Returns400()
    {
        // Act - both params should be rejected
        var response = await _authClient.GetAsync("/api/releases?watchlist=true&packages=some-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetReleases_Authenticated_WithWatchlistButNoReleasesInWindow_ReturnsEmpty()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var user = await db.Users.FirstAsync(u => u.StytchUserId == PatchNotesApiFixture.TestUserId);
        var watchedPkg = new Package { Name = "watched-pkg", Url = "https://github.com/watched/repo", GithubOwner = "watched", GithubRepo = "repo" };
        var defaultPkg = new Package { Name = "default-pkg", Url = "https://github.com/default-owner/default-repo", GithubOwner = "default-owner", GithubRepo = "default-repo" };
        db.Packages.AddRange(watchedPkg, defaultPkg);
        await db.SaveChangesAsync();

        db.Watchlists.Add(new Watchlist { UserId = user.Id, PackageId = watchedPkg.Id });
        await db.SaveChangesAsync();

        // Only default package has releases in window — user's watchlist package does not
        db.Releases.Add(new Release { PackageId = defaultPkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/releases");

        // Assert - user has watchlist, filters to it, but no releases → empty (NOT default)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var releases = json.GetProperty("items");
        releases.GetArrayLength().Should().Be(0);
    }
}
