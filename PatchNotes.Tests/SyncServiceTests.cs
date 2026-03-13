using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatchNotes.Data;
using PatchNotes.Sync.Core.GitHub;
using PatchNotes.Sync.Core.GitHub.Models;
using PatchNotes.Sync.Core;

namespace PatchNotes.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly PatchNotesDbContext _db;
    private readonly Mock<IGitHubClient> _mockGitHub;
    private readonly Mock<ILogger<SyncService>> _mockLogger;
    private readonly SyncService _syncService;

    public SyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<PatchNotesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new PatchNotesDbContext(options);
        _mockGitHub = new Mock<IGitHubClient>();
        _mockLogger = new Mock<ILogger<SyncService>>();
        _syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    #region SyncAllAsync Tests

    [Fact]
    public async Task SyncAllAsync_WithNoPackages_ReturnsEmptyResult()
    {
        var result = await _syncService.SyncAllAsync();

        result.PackagesSynced.Should().Be(0);
        result.ReleasesAdded.Should().Be(0);
        result.Errors.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAllAsync_WithMultiplePackages_SyncsAll()
    {
        // Arrange
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner2/repo2", NpmName = "pkg2", GithubOwner = "owner2", GithubRepo = "repo2" };
        _db.Packages.AddRange(package1, package2);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner1", "repo1", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow)
        ]);
        SetupGitHubReleases("owner2", "repo2", [
            CreateRelease("v2.0.0", DateTimeOffset.UtcNow),
            CreateRelease("v2.1.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncAllAsync();

        // Assert
        result.PackagesSynced.Should().Be(2);
        result.ReleasesAdded.Should().Be(3);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAllAsync_WithPartialFailure_DoesNotSaveReleasesFromFailedPackage()
    {
        // Arrange
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner2/repo2", NpmName = "pkg2", GithubOwner = "owner2", GithubRepo = "repo2" };
        var package3 = new Package { Name = "pkg3", Url = "https://github.com/owner3/repo3", NpmName = "pkg3", GithubOwner = "owner3", GithubRepo = "repo3" };
        _db.Packages.AddRange(package1, package2, package3);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner1", "repo1", [CreateRelease("v1.0.0", DateTimeOffset.UtcNow)]);

        // Package 2: yields one release successfully then throws
        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync("owner2", "repo2", It.IsAny<CancellationToken>()))
            .Returns(PartiallyThrowingAsyncEnumerable(
                [CreateRelease("v2.0.0", DateTimeOffset.UtcNow)],
                "API Error partway through"));

        SetupGitHubReleases("owner3", "repo3", [CreateRelease("v3.0.0", DateTimeOffset.UtcNow)]);

        // Act
        var result = await _syncService.SyncAllAsync();

        // Assert
        result.PackagesSynced.Should().Be(2); // pkg1 and pkg3
        result.Errors.Should().HaveCount(1);
        result.Errors[0].PackageName.Should().Be("pkg2");

        // Key assertion: pkg2's partially-added release should NOT be saved
        var releases = await _db.Releases.ToListAsync();
        releases.Should().HaveCount(2);
        releases.Should().Contain(r => r.Tag == "v1.0.0");
        releases.Should().Contain(r => r.Tag == "v3.0.0");
        releases.Should().NotContain(r => r.Tag == "v2.0.0");
    }

    [Fact]
    public async Task SyncAllAsync_WithFailingPackage_ContinuesAndRecordsError()
    {
        // Arrange
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner2/repo2", NpmName = "pkg2", GithubOwner = "owner2", GithubRepo = "repo2" };
        _db.Packages.AddRange(package1, package2);
        await _db.SaveChangesAsync();

        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync("owner1", "repo1", It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<GitHubRelease>("API Error"));

        SetupGitHubReleases("owner2", "repo2", [CreateRelease("v1.0.0", DateTimeOffset.UtcNow)]);

        // Act
        var result = await _syncService.SyncAllAsync();

        // Assert
        result.PackagesSynced.Should().Be(1);
        result.ReleasesAdded.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].PackageName.Should().Be("pkg1");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAllAsync_GivenCancellationToken_PassesItToGitHubClient()
    {
        // Arrange
        var package = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        CancellationToken? capturedToken = null;

        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((owner, repo, ct) =>
            {
                capturedToken = ct;
                return ToAsyncEnumerable([CreateRelease("v1.0.0", DateTimeOffset.UtcNow)]);
            });

        // Act
        await _syncService.SyncAllAsync(cancellationToken: cts.Token);

        // Assert - verify the cancellation token is properly passed through
        capturedToken.Should().NotBeNull();
        capturedToken!.Value.Should().Be(cts.Token);
    }

    #endregion

    #region SyncPackageAsync Tests

    [Fact]
    public async Task SyncPackageAsync_WithMissingGitHubInfo_ReturnsZeroReleases()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com//repo", NpmName = "pkg", GithubOwner = "", GithubRepo = "repo" };

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(0);
        _mockGitHub.Verify(x => x.GetAllReleasesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncPackageAsync_WithNewReleases_AddsToDatabase()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var publishedAt = DateTimeOffset.UtcNow;
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", publishedAt, "Release 1", "Body 1"),
            CreateRelease("v1.1.0", publishedAt, "Release 2", "Body 2")
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(2);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().HaveCount(2);
        releases.Should().Contain(r => r.Tag == "v1.0.0");
        releases.Should().Contain(r => r.Tag == "v1.1.0");
    }

    [Fact]
    public async Task SyncPackageAsync_GivenDraftRelease_SkipsIt()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow, draft: true),
            CreateRelease("v1.1.0", DateTimeOffset.UtcNow, draft: false)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().ContainSingle(r => r.Tag == "v1.1.0");
    }

    [Fact]
    public async Task SyncPackageAsync_GivenReleaseWithNoPublishedDate_SkipsIt()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            new GitHubRelease { TagName = "v1.0.0", PublishedAt = null },
            CreateRelease("v1.1.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);
    }

    [Fact]
    public async Task SyncPackageAsync_GivenDuplicateTag_SkipsIt()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        // Add existing release
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            FetchedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow),
            CreateRelease("v1.1.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().HaveCount(2);
    }

    [Fact]
    public async Task SyncPackageAsync_StopsAtOlderReleases_WhenLastFetchedAtIsSet()
    {
        // Arrange
        var lastFetched = DateTimeOffset.UtcNow.AddDays(-1);
        var package = new Package
        {
            Name = "pkg",
            Url = "https://github.com/owner/repo",
            NpmName = "pkg",
            GithubOwner = "owner",
            GithubRepo = "repo",
            LastFetchedAt = lastFetched
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.2.0", DateTimeOffset.UtcNow),
            CreateRelease("v1.1.0", lastFetched.AddHours(-1)) // Older than lastFetched - should stop here
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().ContainSingle(r => r.Tag == "v1.2.0");
    }

    [Fact]
    public async Task SyncPackageAsync_GivenSuccessfulSync_UpdatesLastFetchedAt()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var beforeSync = DateTimeOffset.UtcNow;
        SetupGitHubReleases("owner", "repo", []);

        // Act
        await _syncService.SyncPackageAsync(package);

        // Assert
        package.LastFetchedAt.Should().NotBeNull();
        package.LastFetchedAt.Should().BeOnOrAfter(beforeSync);
    }

    [Fact]
    public async Task SyncPackageAsync_GivenNewReleases_ReturnsThemAsNeedingSummary()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var publishedAt = DateTimeOffset.UtcNow;
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", publishedAt, "Release 1", "Body 1"),
            CreateRelease("v1.1.0", publishedAt, "Release 2", "Body 2")
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(2);
        result.ReleasesNeedingSummary.Should().HaveCount(2);
        result.ReleasesNeedingSummary.Should().AllSatisfy(r => r.SummaryStale.Should().BeTrue());
    }

    [Fact]
    public async Task SyncPackageAsync_WithIncludeExistingWithoutSummary_ReturnsAllReleasesWithoutSummary()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        // Add existing release needing summary (stale)
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v0.9.0",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-10),
            FetchedAt = DateTimeOffset.UtcNow.AddDays(-10),
            SummaryStale = true
        });
        // Add existing release with summary (not stale)
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v0.8.0",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-20),
            FetchedAt = DateTimeOffset.UtcNow.AddDays(-20),
            SummaryStale = false
        });
        await _db.SaveChangesAsync();

        var publishedAt = DateTimeOffset.UtcNow;
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", publishedAt, "Release 1", "Body 1")
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package, includeExistingWithoutSummary: true);

        // Assert
        result.ReleasesAdded.Should().Be(1);
        // Should include the new release + the existing one without summary (not the one with summary)
        result.ReleasesNeedingSummary.Should().HaveCount(2);
        result.ReleasesNeedingSummary.Should().Contain(r => r.Tag == "v1.0.0");
        result.ReleasesNeedingSummary.Should().Contain(r => r.Tag == "v0.9.0");
        result.ReleasesNeedingSummary.Should().NotContain(r => r.Tag == "v0.8.0");
    }

    [Fact]
    public async Task SyncAllAsync_GivenMultiplePackages_AggregatesAllReleasesNeedingSummary()
    {
        // Arrange
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner2/repo2", NpmName = "pkg2", GithubOwner = "owner2", GithubRepo = "repo2" };
        _db.Packages.AddRange(package1, package2);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner1", "repo1", [CreateRelease("v1.0.0", DateTimeOffset.UtcNow)]);
        SetupGitHubReleases("owner2", "repo2", [
            CreateRelease("v2.0.0", DateTimeOffset.UtcNow),
            CreateRelease("v2.1.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncAllAsync();

        // Assert
        result.PackagesSynced.Should().Be(2);
        result.ReleasesAdded.Should().Be(3);
        result.ReleasesNeedingSummary.Should().HaveCount(3);
    }

    [Fact]
    public async Task SyncPackageAsync_WithTagPrefix_OnlyIncludesMatchingTags()
    {
        // Arrange - simulates vitejs/vite monorepo
        var package = new Package
        {
            Name = "vite",
            Url = "https://github.com/vitejs/vite",
            NpmName = "vite",
            GithubOwner = "vitejs",
            GithubRepo = "vite",
            TagPrefix = "v"
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("vitejs", "vite", [
            CreateRelease("v7.3.1", DateTimeOffset.UtcNow, "Vite 7.3.1"),
            CreateRelease("v8.0.0-beta.13", DateTimeOffset.UtcNow, "Vite 8.0.0-beta.13"),
            CreateRelease("create-vite@8.0.0", DateTimeOffset.UtcNow, "create-vite 8.0.0"),
            CreateRelease("plugin-react@2.0.0", DateTimeOffset.UtcNow, "plugin-react 2.0.0"),
            CreateRelease("plugin-legacy@8.0.0-beta.3", DateTimeOffset.UtcNow, "plugin-legacy 8.0.0-beta.3")
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert - only v-prefixed tags should be included
        result.ReleasesAdded.Should().Be(2);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().HaveCount(2);
        releases.Should().Contain(r => r.Tag == "v7.3.1");
        releases.Should().Contain(r => r.Tag == "v8.0.0-beta.13");
        releases.Should().NotContain(r => r.Tag == "create-vite@8.0.0");
        releases.Should().NotContain(r => r.Tag == "plugin-react@2.0.0");
    }

    [Fact]
    public async Task SyncPackageAsync_WithTagPrefix_CreateVite()
    {
        // Arrange - track create-vite from the same monorepo
        var package = new Package
        {
            Name = "create-vite",
            Url = "https://github.com/vitejs/vite",
            GithubOwner = "vitejs",
            GithubRepo = "vite",
            TagPrefix = "create-vite@"
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("vitejs", "vite", [
            CreateRelease("v7.3.1", DateTimeOffset.UtcNow, "Vite 7.3.1"),
            CreateRelease("create-vite@8.0.0", DateTimeOffset.UtcNow, "create-vite 8.0.0"),
            CreateRelease("create-vite@7.0.0", DateTimeOffset.UtcNow, "create-vite 7.0.0"),
            CreateRelease("plugin-react@2.0.0", DateTimeOffset.UtcNow, "plugin-react 2.0.0")
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert - only create-vite@ tags
        result.ReleasesAdded.Should().Be(2);

        var releases = await _db.Releases.Where(r => r.PackageId == package.Id).ToListAsync();
        releases.Should().HaveCount(2);
        releases.Should().Contain(r => r.Tag == "create-vite@8.0.0");
        releases.Should().Contain(r => r.Tag == "create-vite@7.0.0");
    }

    [Fact]
    public async Task SyncPackageAsync_WithNullTagPrefix_IncludesAllReleases()
    {
        // Arrange - null TagPrefix means include everything (backward compatible)
        var package = new Package
        {
            Name = "pkg",
            Url = "https://github.com/owner/repo",
            NpmName = "pkg",
            GithubOwner = "owner",
            GithubRepo = "repo",
            TagPrefix = null
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow),
            CreateRelease("some-other-tag", DateTimeOffset.UtcNow),
            CreateRelease("anything@1.0.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert - all tags included
        result.ReleasesAdded.Should().Be(3);
    }

    [Fact]
    public async Task SyncPackageAsync_WithEmptyTagPrefix_IncludesAllReleases()
    {
        // Arrange - empty string TagPrefix also means include everything
        var package = new Package
        {
            Name = "pkg",
            Url = "https://github.com/owner/repo",
            NpmName = "pkg",
            GithubOwner = "owner",
            GithubRepo = "repo",
            TagPrefix = ""
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow),
            CreateRelease("other@1.0.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert - all tags included
        result.ReleasesAdded.Should().Be(2);
    }

    #endregion

    #region GetReleasesNeedingSummaryAsync Tests

    [Fact]
    public async Task GetReleasesNeedingSummaryAsync_GivenReleasesWithNoSummary_ReturnsThem()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), FetchedAt = DateTimeOffset.UtcNow.AddDays(-3), SummaryStale = true },
            new Release { PackageId = package.Id, Tag = "v1.1.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), FetchedAt = DateTimeOffset.UtcNow.AddDays(-2), SummaryStale = false },
            new Release { PackageId = package.Id, Tag = "v1.2.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow.AddDays(-1), SummaryStale = true }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _syncService.GetReleasesNeedingSummaryAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Tag == "v1.0.0");
        result.Should().Contain(r => r.Tag == "v1.2.0");
        result.Should().NotContain(r => r.Tag == "v1.1.0");
    }

    [Fact]
    public async Task GetReleasesNeedingSummaryAsync_GivenMultipleReleases_ReturnsNewestFirst()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v1.1.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v1.2.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _syncService.GetReleasesNeedingSummaryAsync();

        // Assert
        result.Should().HaveCount(3);
        result[0].Tag.Should().Be("v1.1.0"); // Most recent
        result[1].Tag.Should().Be("v1.2.0");
        result[2].Tag.Should().Be("v1.0.0"); // Oldest
    }

    [Fact]
    public async Task GetReleasesNeedingSummaryAsync_GivenStaleSummaryReleases_ReturnsThem()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-3), FetchedAt = DateTimeOffset.UtcNow.AddDays(-3), SummaryStale = true },
            new Release { PackageId = package.Id, Tag = "v1.1.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), FetchedAt = DateTimeOffset.UtcNow.AddDays(-2), SummaryStale = false },
            new Release { PackageId = package.Id, Tag = "v1.2.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow.AddDays(-1), SummaryStale = true }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _syncService.GetReleasesNeedingSummaryAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Tag == "v1.0.0");  // Stale
        result.Should().Contain(r => r.Tag == "v1.2.0");  // Stale
        result.Should().NotContain(r => r.Tag == "v1.1.0"); // Not stale
    }

    [Fact]
    public async Task GetReleasesNeedingSummaryAsync_ByPackageId_ReturnsStaleSummaryReleases()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", NpmName = "pkg", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), FetchedAt = DateTimeOffset.UtcNow.AddDays(-2), SummaryStale = true },
            new Release { PackageId = package.Id, Tag = "v1.1.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow.AddDays(-1), SummaryStale = false }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _syncService.GetReleasesNeedingSummaryAsync(package.Id);

        // Assert
        result.Should().ContainSingle();
        result[0].Tag.Should().Be("v1.0.0");
    }

    [Fact]
    public async Task GetReleasesNeedingSummaryAsync_ByPackageId_ReturnsOnlyForThatPackage()
    {
        // Arrange
        var package1 = new Package { Name = "pkg1", Url = "https://github.com/owner1/repo1", NpmName = "pkg1", GithubOwner = "owner1", GithubRepo = "repo1" };
        var package2 = new Package { Name = "pkg2", Url = "https://github.com/owner2/repo2", NpmName = "pkg2", GithubOwner = "owner2", GithubRepo = "repo2" };
        _db.Packages.AddRange(package1, package2);
        await _db.SaveChangesAsync();

        _db.Releases.AddRange(
            new Release { PackageId = package1.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package1.Id, Tag = "v1.1.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package2.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _syncService.GetReleasesNeedingSummaryAsync(package1.Id);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.PackageId.Should().Be(package1.Id));
    }

    #endregion

    #region SyncRepoAsync Tests

    [Fact]
    public async Task SyncRepoAsync_GivenNewRepo_CreatesPackageAndSyncsReleases()
    {
        // Arrange
        SetupGitHubReleases("prettier", "prettier", [
            CreateRelease("v3.0.0", DateTimeOffset.UtcNow, body: "Release notes"),
            CreateRelease("v2.9.0", DateTimeOffset.UtcNow.AddDays(-1))
        ]);

        // Act
        var result = await _syncService.SyncRepoAsync("prettier", "prettier");

        // Assert
        result.ReleasesAdded.Should().Be(2);

        var package = await _db.Packages.SingleAsync();
        package.Name.Should().Be("prettier");
        package.GithubOwner.Should().Be("prettier");
        package.GithubRepo.Should().Be("prettier");
        package.Url.Should().Be("https://github.com/prettier/prettier");

        var releases = await _db.Releases.ToListAsync();
        releases.Should().HaveCount(2);
    }

    [Fact]
    public async Task SyncRepoAsync_GivenPackageAlreadyInDb_UsesExistingPackage()
    {
        // Arrange
        var existing = new Package
        {
            Name = "prettier",
            Url = "https://github.com/prettier/prettier",
            GithubOwner = "prettier",
            GithubRepo = "prettier"
        };
        _db.Packages.Add(existing);
        await _db.SaveChangesAsync();

        SetupGitHubReleases("prettier", "prettier", [
            CreateRelease("v3.0.0", DateTimeOffset.UtcNow)
        ]);

        // Act
        var result = await _syncService.SyncRepoAsync("prettier", "prettier");

        // Assert
        result.ReleasesAdded.Should().Be(1);

        var packages = await _db.Packages.ToListAsync();
        packages.Should().HaveCount(1);
        packages[0].Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task SyncRepoAsync_WithNoReleases_ReturnsZero()
    {
        // Arrange
        SetupGitHubReleases("owner", "empty-repo", []);

        // Act
        var result = await _syncService.SyncRepoAsync("owner", "empty-repo");

        // Assert
        result.ReleasesAdded.Should().Be(0);

        var package = await _db.Packages.SingleAsync();
        package.GithubOwner.Should().Be("owner");
        package.GithubRepo.Should().Be("empty-repo");
    }

    [Fact]
    public async Task SyncRepoAsync_WhenGitHubFails_RemovesNewlyCreatedPackage()
    {
        // Arrange - GitHub returns 404 for nonexistent repo
        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync("nonexistent", "repo", It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<GitHubRelease>("Not Found"));

        // Act
        var act = () => _syncService.SyncRepoAsync("nonexistent", "repo");

        // Assert - exception propagates and phantom package is cleaned up
        await act.Should().ThrowAsync<Exception>().WithMessage("Not Found");
        var packages = await _db.Packages.ToListAsync();
        packages.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncRepoAsync_WhenGitHubFails_KeepsExistingPackage()
    {
        // Arrange - existing package, GitHub fails during sync
        var existing = new Package
        {
            Name = "myrepo",
            Url = "https://github.com/owner/myrepo",
            GithubOwner = "owner",
            GithubRepo = "myrepo"
        };
        _db.Packages.Add(existing);
        await _db.SaveChangesAsync();

        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync("owner", "myrepo", It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<GitHubRelease>("API Error"));

        // Act
        var act = () => _syncService.SyncRepoAsync("owner", "myrepo");

        // Assert - exception propagates but existing package is NOT removed
        await act.Should().ThrowAsync<Exception>().WithMessage("API Error");
        var packages = await _db.Packages.ToListAsync();
        packages.Should().ContainSingle();
        packages[0].Id.Should().Be(existing.Id);
    }

    #endregion

    #region BackfillVersionFieldsAsync Tests

    [Fact]
    public async Task BackfillVersionFieldsAsync_GivenSemverTags_SetsVersionFields()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.2.3", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v10.0.1", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(2);
        var releases = await _db.Releases.OrderBy(r => r.Tag).ToListAsync();

        releases[0].Tag.Should().Be("v1.2.3");
        releases[0].MajorVersion.Should().Be(1);
        releases[0].MinorVersion.Should().Be(2);
        releases[0].PatchVersion.Should().Be(3);
        releases[0].IsPrerelease.Should().BeFalse();

        releases[1].Tag.Should().Be("v10.0.1");
        releases[1].MajorVersion.Should().Be(10);
        releases[1].MinorVersion.Should().Be(0);
        releases[1].PatchVersion.Should().Be(1);
        releases[1].IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_SetsIsPrerelease_ForPrereleaseTags()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v2.0.0-beta.1", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v3.0.0-rc.2", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(2);
        var releases = await _db.Releases.OrderBy(r => r.Tag).ToListAsync();

        releases[0].MajorVersion.Should().Be(2);
        releases[0].MinorVersion.Should().Be(0);
        releases[0].PatchVersion.Should().Be(0);
        releases[0].IsPrerelease.Should().BeTrue();

        releases[1].MajorVersion.Should().Be(3);
        releases[1].IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_GivenMonorepoTags_SetsVersionFieldsCorrectly()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.Add(
            new Release { PackageId = package.Id, Tag = "create-vite@8.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(1);
        var release = await _db.Releases.SingleAsync();
        release.MajorVersion.Should().Be(8);
        release.MinorVersion.Should().Be(0);
        release.PatchVersion.Should().Be(0);
        release.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_HandlesNonSemverTags_SetsMajorVersionToNegativeOne()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.Add(
            new Release { PackageId = package.Id, Tag = "nightly-2025-01-15", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(1);
        var release = await _db.Releases.SingleAsync();
        release.MajorVersion.Should().Be(-1);
        release.MinorVersion.Should().Be(0);
        release.PatchVersion.Should().Be(0);
        release.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_IsIdempotent_ReturnsZeroOnSecondRun()
    {
        // Arrange
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v2.0.0-alpha.1", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "not-a-version", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act - first run
        var firstRun = await _syncService.BackfillVersionFieldsAsync();
        // Capture values after first run
        var releasesAfterFirst = await _db.Releases.OrderBy(r => r.Tag).ToListAsync();
        var firstRunValues = releasesAfterFirst.Select(r => (r.MajorVersion, r.MinorVersion, r.PatchVersion, r.IsPrerelease)).ToList();

        // Act - second run
        var secondRun = await _syncService.BackfillVersionFieldsAsync();
        // Capture values after second run
        var releasesAfterSecond = await _db.Releases.OrderBy(r => r.Tag).ToListAsync();
        var secondRunValues = releasesAfterSecond.Select(r => (r.MajorVersion, r.MinorVersion, r.PatchVersion, r.IsPrerelease)).ToList();

        // Assert
        firstRun.Should().Be(3);
        secondRun.Should().Be(0);
        secondRunValues.Should().BeEquivalentTo(firstRunValues);
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_DoesNotSave_WhenNoChangesNeeded()
    {
        // Arrange - seed releases with already-correct version fields
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v5.3.1",
            PublishedAt = DateTimeOffset.UtcNow,
            FetchedAt = DateTimeOffset.UtcNow,
            MajorVersion = 5,
            MinorVersion = 3,
            PatchVersion = 1,
            IsPrerelease = false
        });
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert - no changes needed, so updated count should be 0
        updated.Should().Be(0);
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_WithMixedTags_CorrectlyParsesAll()
    {
        // Arrange - mix of semver, monorepo, prerelease, and non-semver
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.AddRange(
            new Release { PackageId = package.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "v2.1.0-beta.3", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "@scope/pkg@3.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow },
            new Release { PackageId = package.Id, Tag = "random-tag", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(4);
        var releases = await _db.Releases.ToListAsync();
        var byTag = releases.ToDictionary(r => r.Tag);

        // Standard semver
        byTag["v1.0.0"].MajorVersion.Should().Be(1);
        byTag["v1.0.0"].MinorVersion.Should().Be(0);
        byTag["v1.0.0"].PatchVersion.Should().Be(0);
        byTag["v1.0.0"].IsPrerelease.Should().BeFalse();

        // Prerelease
        byTag["v2.1.0-beta.3"].MajorVersion.Should().Be(2);
        byTag["v2.1.0-beta.3"].MinorVersion.Should().Be(1);
        byTag["v2.1.0-beta.3"].PatchVersion.Should().Be(0);
        byTag["v2.1.0-beta.3"].IsPrerelease.Should().BeTrue();

        // Monorepo scoped
        byTag["@scope/pkg@3.0.0"].MajorVersion.Should().Be(3);
        byTag["@scope/pkg@3.0.0"].MinorVersion.Should().Be(0);
        byTag["@scope/pkg@3.0.0"].PatchVersion.Should().Be(0);
        byTag["@scope/pkg@3.0.0"].IsPrerelease.Should().BeFalse();

        // Non-semver
        byTag["random-tag"].MajorVersion.Should().Be(-1);
        byTag["random-tag"].MinorVersion.Should().Be(0);
        byTag["random-tag"].PatchVersion.Should().Be(0);
        byTag["random-tag"].IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_GivenMixOfCorrectAndWrongFields_OnlyUpdatesWrong()
    {
        // Arrange - one release already correct, one needs updating
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.AddRange(
            new Release
            {
                PackageId = package.Id,
                Tag = "v1.0.0",
                PublishedAt = DateTimeOffset.UtcNow,
                FetchedAt = DateTimeOffset.UtcNow,
                MajorVersion = 1,
                MinorVersion = 0,
                PatchVersion = 0,
                IsPrerelease = false // already correct
            },
            new Release
            {
                PackageId = package.Id,
                Tag = "v2.0.0",
                PublishedAt = DateTimeOffset.UtcNow,
                FetchedAt = DateTimeOffset.UtcNow,
                MajorVersion = 0,
                MinorVersion = 0,
                PatchVersion = 0,
                IsPrerelease = false // needs update
            }
        );
        await _db.SaveChangesAsync();

        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert - only the one that needed updating
        updated.Should().Be(1);

        var release = await _db.Releases.SingleAsync(r => r.Tag == "v2.0.0");
        release.MajorVersion.Should().Be(2);
    }

    [Fact]
    public async Task BackfillVersionFieldsAsync_WithNoReleases_ReturnsZero()
    {
        // Act
        var updated = await _syncService.BackfillVersionFieldsAsync();

        // Assert
        updated.Should().Be(0);
    }

    #endregion

    #region ReResolveStaleChangelogsAsync Tests

    [Fact]
    public async Task ReResolveStaleChangelogsAsync_WithoutResolver_ReturnsZero()
    {
        var result = await _syncService.ReResolveStaleChangelogsAsync();
        result.Should().Be(0);
    }

    [Fact]
    public async Task ReResolveStaleChangelogsAsync_UpdatesBody_WhenBetterContentAvailable()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "vite", Url = "https://github.com/vitejs/vite", GithubOwner = "vitejs", GithubRepo = "vite" };
        _db.Packages.Add(package);
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v8.0.0",
            Body = "### Features\n\n* update rolldown",
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ChangelogStale = true,
            SummaryStale = false
        });
        await _db.SaveChangesAsync();

        var fullChangelog = "## 8.0.0\n\nVite 8 is here! Full announcement with breaking changes and features.\n\n## 7.0.0\n\nOld version.";
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("vitejs", "vite", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullChangelog);

        // Act
        var result = await syncService.ReResolveStaleChangelogsAsync();

        // Assert
        result.Should().Be(1);
        var release = await _db.Releases.SingleAsync();
        release.Body.Should().Contain("Vite 8 is here!");
        release.ChangelogStale.Should().BeFalse();
        release.SummaryStale.Should().BeTrue();
    }

    [Fact]
    public async Task ReResolveStaleChangelogsAsync_ExpiresOldReleases()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Body = "### Features\n\n* some feature",
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-13), // older than 12h
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-13),
            ChangelogStale = true
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await syncService.ReResolveStaleChangelogsAsync();

        // Assert - expired, not updated
        result.Should().Be(0);
        var release = await _db.Releases.SingleAsync();
        release.ChangelogStale.Should().BeFalse();
        release.Body.Should().Contain("some feature"); // unchanged
    }

    [Fact]
    public async Task ReResolveStaleChangelogsAsync_LeavesFlag_WhenNoImprovement()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Body = "### Features\n\n* some feature",
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-1),
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ChangelogStale = true
        });
        await _db.SaveChangesAsync();

        // Resolver returns same short content
        var changelog = "## 1.0.0\n\n### Features\n\n* some feature\n\n## 0.9.0\n\nOld.";
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        // Act
        var result = await syncService.ReResolveStaleChangelogsAsync();

        // Assert - no improvement, flag stays
        result.Should().Be(0);
        var release = await _db.Releases.SingleAsync();
        release.ChangelogStale.Should().BeTrue();
    }

    [Fact]
    public async Task SyncPackageAsync_FlagsChangelogStale_WhenResolvedToConventionalCommits()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "vite", Url = "https://github.com/vitejs/vite", GithubOwner = "vitejs", GithubRepo = "vite" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v8.0.0/packages/vite/CHANGELOG.md) for details.";
        SetupGitHubReleases("vitejs", "vite", [
            CreateRelease("v8.0.0", DateTimeOffset.UtcNow, "Vite 8.0.0", referenceBody)
        ]);

        // Changelog only has short conventional-commits content
        var changelog = "## 8.0.0\n\n### Features\n\n* update rolldown\n\n## 7.0.0\n\nOld.";
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("vitejs", "vite", "packages/vite/CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync();
        release.ChangelogStale.Should().BeTrue();
        release.Body.Should().Contain("update rolldown");
    }

    [Fact]
    public async Task SyncPackageAsync_DoesNotFlagChangelogStale_WhenBodyIsLong()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "vite", Url = "https://github.com/vitejs/vite", GithubOwner = "vitejs", GithubRepo = "vite" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v8.0.0/packages/vite/CHANGELOG.md) for details.";
        SetupGitHubReleases("vitejs", "vite", [
            CreateRelease("v8.0.0", DateTimeOffset.UtcNow, "Vite 8.0.0", referenceBody)
        ]);

        // Full announcement
        var longContent = "Vite 8 is here! " + new string('x', 600);
        var changelog = $"## 8.0.0\n\n{longContent}\n\n## 7.0.0\n\nOld.";
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("vitejs", "vite", "packages/vite/CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync();
        release.ChangelogStale.Should().BeFalse();
    }

    #endregion

    #region SyncPackageAsync + ChangelogResolver Integration Tests

    [Fact]
    public async Task SyncPackageAsync_WithChangelogResolver_ResolvesChangelogReferenceBody()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "vite", Url = "https://github.com/vitejs/vite", GithubOwner = "vitejs", GithubRepo = "vite" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "Please refer to [CHANGELOG.md](https://github.com/vitejs/vite/blob/v7.3.1/packages/vite/CHANGELOG.md) for details.";
        SetupGitHubReleases("vitejs", "vite", [
            CreateRelease("v7.3.1", DateTimeOffset.UtcNow, "Vite 7.3.1", referenceBody)
        ]);

        var changelog = """
            ## 7.3.1

            - Fixed HMR regression
            - Improved build performance

            ## 7.3.0

            Previous version.
            """;
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("vitejs", "vite", "packages/vite/CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Contain("Fixed HMR regression");
        release.Body.Should().Contain("Improved build performance");
        release.Body.Should().NotContain("refer to");
    }

    [Fact]
    public async Task SyncPackageAsync_WithChangelogResolver_KeepsOriginalBody_WhenResolutionReturnsNull()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "See CHANGELOG.md for details";
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow, body: referenceBody)
        ]);

        // All changelog file lookups return null (file not found)
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert - original body is preserved when resolver returns null
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Be(referenceBody);
    }

    [Fact]
    public async Task SyncPackageAsync_WithChangelogResolver_KeepsOriginalBody_WhenResolutionThrows()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "See CHANGELOG.md for full details";
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow, body: referenceBody)
        ]);

        // Changelog fetch throws
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert - original body preserved, no exception propagated
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Be(referenceBody);
    }

    [Fact]
    public async Task SyncPackageAsync_WithChangelogResolver_DoesNotResolve_WhenBodyIsRealContent()
    {
        // Arrange
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var realBody = "## Bug Fixes\n\n- Fixed authentication issue\n- Fixed memory leak in worker pool\n\n## Features\n\n- Added dark mode support";
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow, body: realBody)
        ]);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert - body is kept as-is, no file content fetched
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Be(realBody);
        _mockGitHub.Verify(
            x => x.GetFileContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncPackageAsync_WithChangelogResolver_ResolvesViaFallbackPath()
    {
        // Arrange - body references changelog but no URL path, so resolver uses standard paths
        var mockChangelogLogger = new Mock<ILogger<ChangelogResolver>>();
        var changelogResolver = new ChangelogResolver(_mockGitHub.Object, mockChangelogLogger.Object);
        var syncService = new SyncService(_db, _mockGitHub.Object, _mockLogger.Object, changelogResolver);

        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "See CHANGELOG.md for details";
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v2.0.0", DateTimeOffset.UtcNow, body: referenceBody)
        ]);

        var changelog = """
            ## 2.0.0

            Breaking: Removed deprecated API.

            ## 1.0.0

            Initial release.
            """;
        _mockGitHub
            .Setup(x => x.GetFileContentAsync("owner", "repo", "CHANGELOG.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(changelog);

        // Act
        var result = await syncService.SyncPackageAsync(package);

        // Assert
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Contain("Removed deprecated API");
        release.Body.Should().NotContain("See CHANGELOG.md");
    }

    [Fact]
    public async Task SyncPackageAsync_WithoutChangelogResolver_DoesNotAttemptResolution()
    {
        // Arrange - using the default _syncService which has no ChangelogResolver
        var package = new Package { Name = "pkg", Url = "https://github.com/owner/repo", GithubOwner = "owner", GithubRepo = "repo" };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var referenceBody = "See CHANGELOG.md for details";
        SetupGitHubReleases("owner", "repo", [
            CreateRelease("v1.0.0", DateTimeOffset.UtcNow, body: referenceBody)
        ]);

        // Act
        var result = await _syncService.SyncPackageAsync(package);

        // Assert - body is kept as-is, no file content fetched
        result.ReleasesAdded.Should().Be(1);
        var release = await _db.Releases.SingleAsync(r => r.PackageId == package.Id);
        release.Body.Should().Be(referenceBody);
        _mockGitHub.Verify(
            x => x.GetFileContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Helper Methods

    private void SetupGitHubReleases(string owner, string repo, List<GitHubRelease> releases)
    {
        _mockGitHub
            .Setup(x => x.GetAllReleasesAsync(owner, repo, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(releases));
    }

    private static GitHubRelease CreateRelease(
        string tag,
        DateTimeOffset publishedAt,
        string? name = null,
        string? body = null,
        bool draft = false)
    {
        return new GitHubRelease
        {
            Id = Random.Shared.NextInt64(),
            TagName = tag,
            Name = name ?? tag,
            Body = body,
            Draft = draft,
            PublishedAt = publishedAt
        };
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<T> PartiallyThrowingAsyncEnumerable<T>(IEnumerable<T> items, string message)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
        throw new Exception(message);
    }

    private static async IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(string message)
    {
        await Task.CompletedTask;
        throw new Exception(message);
#pragma warning disable CS0162 // Unreachable code detected
        yield break; // Never reached, but required for compiler
#pragma warning restore CS0162
    }

    #endregion
}
