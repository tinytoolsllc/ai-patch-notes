using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;
using PatchNotes.Api.Routes;

namespace PatchNotes.Tests;

public class FeedApiTests : IAsyncLifetime
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

    [Fact]
    public async Task GetFeed_GivenReleasesOutsideWindow_ExcludesThemFromFeed()
    {
        // Arrange: create a package with releases spanning >7 days
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "test-pkg",
            Url = "https://github.com/owner/test-pkg",
            NpmName = "test-pkg",
            GithubOwner = "owner",
            GithubRepo = "test-pkg"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var latestDate = DateTimeOffset.UtcNow;
        // Recent release (within window)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.2.0",
            Title = "Recent",
            PublishedAt = latestDate,
            FetchedAt = latestDate,
            MajorVersion = 1,
            MinorVersion = 2,
            PatchVersion = 0
        });
        // Release within window (5 days ago)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.1.0",
            Title = "Within window",
            PublishedAt = latestDate.AddDays(-5),
            FetchedAt = latestDate,
            MajorVersion = 1,
            MinorVersion = 1,
            PatchVersion = 0
        });
        // Old release (outside 7-day window)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Title = "Old",
            PublishedAt = latestDate.AddDays(-30),
            FetchedAt = latestDate,
            MajorVersion = 1,
            MinorVersion = 0,
            PatchVersion = 0
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/feed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        feed.Should().NotBeNull();
        feed!.Groups.Should().ContainSingle();

        var group = feed.Groups[0];
        // Only 2 releases within 7-day window of latest
        group.Releases.Should().HaveCount(2);
        group.Releases.Select(r => r.Tag).Should().Contain("v1.2.0");
        group.Releases.Select(r => r.Tag).Should().Contain("v1.1.0");
        group.Releases.Select(r => r.Tag).Should().NotContain("v1.0.0");
        // ReleaseCount reflects total count (3)
        group.ReleaseCount.Should().Be(3);
    }

    [Fact]
    public async Task GetFeed_GivenMixedReleases_FiltersToCurrentStableAndFuturePrereleases()
    {
        // Arrange: .NET-like scenario: stable v9, prereleases v10+v11, old v8/v7
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "dotnet-runtime",
            Url = "https://github.com/dotnet/runtime",
            GithubOwner = "dotnet",
            GithubRepo = "runtime"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        // Old stable versions (should be hidden)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v7.0.0",
            Title = "v7 stable",
            PublishedAt = now.AddDays(-1),
            FetchedAt = now,
            MajorVersion = 7,
            MinorVersion = 0,
            PatchVersion = 0
        });
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v8.0.0",
            Title = "v8 stable",
            PublishedAt = now.AddDays(-1),
            FetchedAt = now,
            MajorVersion = 8,
            MinorVersion = 0,
            PatchVersion = 0
        });
        // Current stable (should be shown)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v9.0.0",
            Title = "v9 stable",
            PublishedAt = now,
            FetchedAt = now,
            MajorVersion = 9,
            MinorVersion = 0,
            PatchVersion = 0
        });
        // Future prereleases (should be shown)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v10.0.0-preview.1",
            Title = "v10 preview",
            PublishedAt = now,
            FetchedAt = now,
            MajorVersion = 10,
            MinorVersion = 0,
            PatchVersion = 0,
            IsPrerelease = true
        });
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v11.0.0-preview.1",
            Title = "v11 preview",
            PublishedAt = now,
            FetchedAt = now,
            MajorVersion = 11,
            MinorVersion = 0,
            PatchVersion = 0,
            IsPrerelease = true
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/feed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        feed.Should().NotBeNull();

        var groupVersions = feed!.Groups
            .Select(g => (g.MajorVersion, g.IsPrerelease))
            .ToList();

        // Should show: v9 stable, v10 prerelease, v11 prerelease
        groupVersions.Should().Contain((9, false));
        groupVersions.Should().Contain((10, true));
        groupVersions.Should().Contain((11, true));
        // Should NOT show: v7 or v8
        groupVersions.Should().NotContain((7, false));
        groupVersions.Should().NotContain((8, false));
    }

    [Fact]
    public async Task GetFeed_GivenPrereleaseAtSameMajor_IncludesPrereleaseInFeed()
    {
        // Arrange: prereleases at same major version as stable should be shown
        // (e.g. Next.js v16.2.0-canary.47 alongside stable v16.1.6)
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "pkg-with-old-pre",
            Url = "https://github.com/owner/pkg-with-old-pre",
            GithubOwner = "owner",
            GithubRepo = "pkg-with-old-pre"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        // v0 stable
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v0.100.0",
            Title = "v0 stable",
            PublishedAt = now,
            FetchedAt = now,
            MajorVersion = 0,
            MinorVersion = 100,
            PatchVersion = 0
        });
        // v0 prerelease (same major as stable - should be shown)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v0.99.0-beta.1",
            Title = "v0 beta",
            PublishedAt = now.AddDays(-1),
            FetchedAt = now,
            MajorVersion = 0,
            MinorVersion = 99,
            PatchVersion = 0,
            IsPrerelease = true
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/feed");

        // Assert
        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        feed.Should().NotBeNull();

        var packageGroups = feed!.Groups.Where(g => g.PackageId == package.Id).ToList();
        // Both the stable v0 group and the prerelease v0 group should appear
        packageGroups.Should().HaveCount(2);
        packageGroups.Should().Contain(g => !g.IsPrerelease && g.MajorVersion == 0);
        packageGroups.Should().Contain(g => g.IsPrerelease && g.MajorVersion == 0);
    }

    [Fact]
    public async Task GetFeed_ShowsHighestPrereleaseGroup_WhenNoStableExists()
    {
        // Arrange: package with only prereleases
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "prerelease-only",
            Url = "https://github.com/owner/prerelease-only",
            GithubOwner = "owner",
            GithubRepo = "prerelease-only"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        // v1 prerelease (old, should be hidden)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0-alpha.1",
            Title = "v1 alpha",
            PublishedAt = now.AddDays(-1),
            FetchedAt = now,
            MajorVersion = 1,
            MinorVersion = 0,
            PatchVersion = 0,
            IsPrerelease = true
        });
        // v2 prerelease (highest, should be shown)
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v2.0.0-alpha.1",
            Title = "v2 alpha",
            PublishedAt = now,
            FetchedAt = now,
            MajorVersion = 2,
            MinorVersion = 0,
            PatchVersion = 0,
            IsPrerelease = true
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/feed");

        // Assert
        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        feed.Should().NotBeNull();

        var packageGroups = feed!.Groups.Where(g => g.PackageId == package.Id).ToList();
        packageGroups.Should().ContainSingle();
        packageGroups[0].MajorVersion.Should().Be(2);
        packageGroups[0].IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public async Task GetFeed_WhenDefaultFeedCached_SkipsAuthentication()
    {
        // Arrange: create a package with a release so the feed is non-empty
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "cache-test-pkg",
            Url = "https://github.com/owner/cache-test-pkg",
            GithubOwner = "owner",
            GithubRepo = "cache-test-pkg"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Title = "First release",
            PublishedAt = DateTimeOffset.UtcNow,
            FetchedAt = DateTimeOffset.UtcNow,
            MajorVersion = 1,
            MinorVersion = 0,
            PatchVersion = 0
        });
        await db.SaveChangesAsync();

        // Warm the cache with an anonymous request
        var warmResponse = await _client.GetAsync("/api/feed");
        warmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.MockStytch.AuthenticateCallCount.Should().Be(0, "anonymous request should not call Stytch");

        // Act: authenticated request with no watchlist — cache should be hit before auth
        using var authClient = _fixture.CreateNonAdminClient();
        var authResponse = await authClient.GetAsync("/api/feed");

        // Assert: cache was served, Stytch was never called
        authResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.MockStytch.AuthenticateCallCount.Should().Be(0,
            "cache hit should prevent Stytch authentication for empty-watchlist user");
    }

    [Fact]
    public async Task GetFeed_FallsBackToLatestRelease_WhenWindowEmpty()
    {
        // Arrange: create releases where all are very old and >7 days apart
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "old-pkg",
            Url = "https://github.com/owner/old-pkg",
            NpmName = "old-pkg",
            GithubOwner = "owner",
            GithubRepo = "old-pkg"
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var baseDate = DateTimeOffset.UtcNow.AddDays(-100);
        // Two releases, each >7 days apart from each other
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v2.1.0",
            Title = "Latest old",
            PublishedAt = baseDate,
            FetchedAt = baseDate,
            MajorVersion = 2,
            MinorVersion = 1,
            PatchVersion = 0
        });
        db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v2.0.0",
            Title = "Older",
            PublishedAt = baseDate.AddDays(-30),
            FetchedAt = baseDate,
            MajorVersion = 2,
            MinorVersion = 0,
            PatchVersion = 0
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/feed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        feed.Should().NotBeNull();
        feed!.Groups.Should().ContainSingle();

        var group = feed.Groups[0];
        // Falls back to at least the latest release
        group.Releases.Should().ContainSingle();
        group.Releases[0].Tag.Should().Be("v2.1.0");
        group.ReleaseCount.Should().Be(2);
    }
}
