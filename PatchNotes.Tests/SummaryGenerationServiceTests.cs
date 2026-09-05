using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatchNotes.Data;
using PatchNotes.Sync.Core;
using PatchNotes.Sync.Core.AI;

namespace PatchNotes.Tests;

public class SummaryGenerationServiceTests : IDisposable
{
    private readonly PatchNotesDbContext _db;
    private readonly Mock<IAiClient> _mockAiClient;
    private readonly VersionGroupingService _groupingService;
    private readonly Mock<ILogger<SummaryGenerationService>> _mockLogger;
    private readonly SummaryGenerationService _service;

    public SummaryGenerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<PatchNotesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new PatchNotesDbContext(options);
        _mockAiClient = new Mock<IAiClient>();
        _groupingService = new VersionGroupingService();
        _mockLogger = new Mock<ILogger<SummaryGenerationService>>();
        _service = new SummaryGenerationService(_db, _mockAiClient.Object, _groupingService, _mockLogger.Object);

        // Default: AI client returns a summary
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated summary for the version group.");
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    #region GenerateGroupSummariesAsync Tests

    [Fact]
    public async Task GenerateGroupSummariesAsync_WithNoReleases_ReturnsEmptyResult()
    {
        var package = await CreatePackage();

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(0);
        result.GroupsSkipped.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenGroupWithNoExistingSummary_CreatesNewSummary()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "First release", "Initial features");
        await AddRelease(package.Id, "v1.1.0", "Second release", "Bug fixes");

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(1);
        result.Errors.Should().BeEmpty();

        var summaries = await _db.ReleaseSummaries
            .Where(s => s.PackageId == package.Id)
            .ToListAsync();
        summaries.Should().ContainSingle();
        summaries[0].MajorVersion.Should().Be(1);
        summaries[0].IsPrerelease.Should().BeFalse();
        summaries[0].Summary.Should().Be("Generated summary for the version group.");
        summaries[0].GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenMultipleGroups_CreatesASummaryForEach()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "v1 release", "Features");
        await AddRelease(package.Id, "v2.0.0", "v2 release", "New features");
        await AddRelease(package.Id, "v2.1.0-beta.1", "v2 beta", "Beta features");

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        // v1 stable, v2 stable, v2 prerelease = 3 groups
        result.SummariesGenerated.Should().Be(3);

        var summaries = await _db.ReleaseSummaries
            .Where(s => s.PackageId == package.Id)
            .OrderBy(s => s.MajorVersion)
            .ThenBy(s => s.IsPrerelease)
            .ToListAsync();
        summaries.Should().HaveCount(3);
        summaries[0].MajorVersion.Should().Be(1);
        summaries[0].IsPrerelease.Should().BeFalse();
        summaries[1].MajorVersion.Should().Be(2);
        summaries[1].IsPrerelease.Should().BeFalse();
        summaries[2].MajorVersion.Should().Be(2);
        summaries[2].IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenGroupWithExistingSummary_UpdatesIt()
    {
        var package = await CreatePackage();
        var generatedAt = DateTimeOffset.UtcNow.AddDays(-5);

        _db.ReleaseSummaries.Add(new ReleaseSummary
        {
            PackageId = package.Id,
            MajorVersion = 1,
            IsPrerelease = false,
            Summary = "Old summary",
            GeneratedAt = generatedAt
        });
        await _db.SaveChangesAsync();

        // Add a new stale release to trigger regeneration
        await AddRelease(package.Id, "v1.2.0", "New release", "New features");

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(1);

        var summaries = await _db.ReleaseSummaries
            .Where(s => s.PackageId == package.Id)
            .ToListAsync();
        summaries.Should().ContainSingle();
        summaries[0].Summary.Should().Be("Generated summary for the version group.");
        summaries[0].GeneratedAt.Should().Be(generatedAt); // Original time preserved
        summaries[0].UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenGroupWithNoStaleReleases_SkipsGroup()
    {
        var package = await CreatePackage();

        // Add a release that already has a summary and is not stale
        _db.Releases.Add(new Release
        {
            PackageId = package.Id,
            Tag = "v1.0.0",
            Title = "Release 1",
            Body = "Features",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
            FetchedAt = DateTimeOffset.UtcNow.AddDays(-5),
            SummaryStale = false,
            MajorVersion = 1,
            MinorVersion = 0,
            PatchVersion = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(0);
        result.GroupsSkipped.Should().Be(1);

        _mockAiClient.Verify(
            x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenStaleReleases_MarksThemNotStaleAfterSummary()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");
        await AddRelease(package.Id, "v1.1.0", "Release 2", "More features");

        await _service.GenerateGroupSummariesAsync(package.Id);

        var releases = await _db.Releases
            .Where(r => r.PackageId == package.Id)
            .ToListAsync();
        releases.Should().AllSatisfy(r => r.SummaryStale.Should().BeFalse());
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_HandlesAiClientError_GracefullySkips()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");
        await AddRelease(package.Id, "v2.0.0", "Release 2", "Features 2");

        // First call succeeds, second fails
        var callCount = 0;
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 2)
                    throw new HttpRequestException("AI provider unavailable");
                return "Generated summary.";
            });

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("AI provider unavailable");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenBadRequest_MarksReleasesNotStaleToBreakRetryLoop()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");

        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Bad Request", null, System.Net.HttpStatusCode.BadRequest));

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(0);
        result.Errors.Should().HaveCount(1);

        // Releases should be marked not stale — no infinite retry
        var releases = await _db.Releases
            .Where(r => r.PackageId == package.Id)
            .ToListAsync();
        releases.Should().AllSatisfy(r => r.SummaryStale.Should().BeFalse());
    }


    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenTooManyRequests_LeavesReleasesStaleAndFlagsRateLimited()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");

        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests));

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(0);
        result.RateLimited.Should().BeTrue();
        result.Errors.Should().HaveCount(1);

        // Unlike the 400 path, the work is still valid — it stays queued for when quota returns.
        var releases = await _db.Releases
            .Where(r => r.PackageId == package.Id)
            .ToListAsync();
        releases.Should().AllSatisfy(r => r.SummaryStale.Should().BeTrue());
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenTooManyRequests_StopsAfterTheFirstRefusal()
    {
        // Two major versions means two groups, so without the early stop this package alone would
        // make a second call against a limit that is already exhausted.
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");
        await AddRelease(package.Id, "v2.0.0", "Release 2", "More features");

        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests));

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.RateLimited.Should().BeTrue();
        result.Errors.Should().HaveCount(1);

        _mockAiClient.Verify(
            x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateAllSummariesAsync_GivenTooManyRequests_StopsWithoutTouchingRemainingPackages()
    {
        // Three queued packages, all refused. The old behaviour attempted every one of them on every
        // run, which is how a backlog of failing summaries turns into a flood of refused requests.
        var first = await CreatePackage("pkg-a");
        var second = await CreatePackage("pkg-b");
        var third = await CreatePackage("pkg-c");
        await AddRelease(first.Id, "v1.0.0", "A", "Body");
        await AddRelease(second.Id, "v1.0.0", "B", "Body");
        await AddRelease(third.Id, "v1.0.0", "C", "Body");

        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Too Many Requests", null, System.Net.HttpStatusCode.TooManyRequests));

        var result = await _service.GenerateAllSummariesAsync();

        result.RateLimited.Should().BeTrue();

        // One call total, not one per package.
        _mockAiClient.Verify(
            x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Everything stays queued, including the packages never attempted.
        var stale = await _db.Releases.CountAsync(r => r.SummaryStale);
        stale.Should().Be(3);
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenTransientError_LeavesReleasesStaleForRetry()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release 1", "Features");

        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var result = await _service.GenerateGroupSummariesAsync(package.Id);

        result.SummariesGenerated.Should().Be(0);
        result.Errors.Should().HaveCount(1);

        // Releases should remain stale — will be retried next run
        var releases = await _db.Releases
            .Where(r => r.PackageId == package.Id)
            .ToListAsync();
        releases.Should().AllSatisfy(r => r.SummaryStale.Should().BeTrue());
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_TruncatesLongReleaseBodies()
    {
        var package = await CreatePackage();
        var longBody = new string('x', 10000);
        await AddRelease(package.Id, "v1.0.0", "Release 1", longBody);

        IReadOnlyList<ReleaseInput>? capturedReleases = null;
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<ReleaseInput>, CancellationToken>((_, releases, _) =>
                capturedReleases = releases)
            .ReturnsAsync("Summary");

        await _service.GenerateGroupSummariesAsync(package.Id);

        capturedReleases.Should().NotBeNull();
        capturedReleases![0].Body!.Length.Should().BeLessThan(5000);
        capturedReleases[0].Body.Should().EndWith("[truncated]");
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenMultipleReleases_PassesAggregatedContentToAiClient()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Initial Release", "First features");
        await AddRelease(package.Id, "v1.1.0", "Patch Release", "Bug fixes and improvements");

        string? capturedPackageName = null;
        IReadOnlyList<ReleaseInput>? capturedReleases = null;
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<ReleaseInput>, CancellationToken>((name, releases, _) =>
            {
                capturedPackageName = name;
                capturedReleases = releases;
            })
            .ReturnsAsync("Summary");

        await _service.GenerateGroupSummariesAsync(package.Id);

        capturedPackageName.Should().Be("test-pkg");
        capturedReleases.Should().NotBeNull();
        capturedReleases.Should().HaveCount(2);
        capturedReleases!.Select(r => r.Tag).Should().Contain("v1.0.0");
        capturedReleases.Select(r => r.Tag).Should().Contain("v1.1.0");
        capturedReleases.SelectMany(r => new[] { r.Body }).Should().Contain("Bug fixes and improvements");
        capturedReleases.SelectMany(r => new[] { r.Body }).Should().Contain("First features");
    }

    [Fact]
    public async Task GenerateGroupSummariesAsync_GivenCancellationRequested_StopsProcessing()
    {
        var package = await CreatePackage();
        await AddRelease(package.Id, "v1.0.0", "Release", "Body");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _service.GenerateGroupSummariesAsync(package.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region GenerateAllSummariesAsync Tests

    [Fact]
    public async Task GenerateAllSummariesAsync_GivenPackagesWithStaleReleases_ProcessesAll()
    {
        var package1 = await CreatePackage("pkg1");
        var package2 = await CreatePackage("pkg2");
        await AddRelease(package1.Id, "v1.0.0", "Release 1", "Features 1");
        await AddRelease(package2.Id, "v2.0.0", "Release 2", "Features 2");

        var result = await _service.GenerateAllSummariesAsync();

        result.SummariesGenerated.Should().Be(2);
        result.Errors.Should().BeEmpty();

        var summaries = await _db.ReleaseSummaries.ToListAsync();
        summaries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateAllSummariesAsync_GivenPackageWithNoStaleReleases_SkipsIt()
    {
        var package1 = await CreatePackage("pkg1");
        var package2 = await CreatePackage("pkg2");

        // pkg1 has stale releases
        await AddRelease(package1.Id, "v1.0.0", "Release 1", "Features 1");

        // pkg2 has no stale releases
        _db.Releases.Add(new Release
        {
            PackageId = package2.Id,
            Tag = "v2.0.0",
            Title = "Release 2",
            Body = "Features 2",
            PublishedAt = DateTimeOffset.UtcNow,
            FetchedAt = DateTimeOffset.UtcNow,
            SummaryStale = false,
            MajorVersion = 2,
            MinorVersion = 0,
            PatchVersion = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.GenerateAllSummariesAsync();

        result.SummariesGenerated.Should().Be(1);

        var summaries = await _db.ReleaseSummaries.ToListAsync();
        summaries.Should().ContainSingle();
        summaries[0].PackageId.Should().Be(package1.Id);
    }

    [Fact]
    public async Task GenerateAllSummariesAsync_WithNoStaleReleases_ReturnsEmptyResult()
    {
        var result = await _service.GenerateAllSummariesAsync();

        result.SummariesGenerated.Should().Be(0);
        result.GroupsSkipped.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Summary Window Anchoring Tests

    [Fact]
    public async Task GenerateGroupSummaryAsync_AnchorsWindowFromLatestRelease_NotUtcNow()
    {
        // Arrange: all releases are 30 days old but within 7 days of each other
        var package = await CreatePackage();
        var latestDate = DateTimeOffset.UtcNow.AddDays(-30);
        await AddRelease(package.Id, "v1.0.0", "Old release 1", "Features A", latestDate.AddDays(-5));
        await AddRelease(package.Id, "v1.1.0", "Old release 2", "Features B", latestDate.AddDays(-3));
        await AddRelease(package.Id, "v1.2.0", "Old release 3", "Features C", latestDate);

        IReadOnlyList<ReleaseInput>? capturedReleases = null;
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<ReleaseInput>, CancellationToken>((_, releases, _) =>
                capturedReleases = releases)
            .ReturnsAsync("Summary");

        // Act
        await _service.GenerateGroupSummariesAsync(package.Id);

        // Assert: all 3 releases should be included (within 7 days of latest)
        // If window were anchored from UtcNow, none would be included (all >7 days old)
        capturedReleases.Should().NotBeNull();
        capturedReleases.Should().HaveCount(3);
    }

    [Fact]
    public async Task GenerateGroupSummaryAsync_GivenReleasesOutsideWindow_ExcludesThemFromSummary()
    {
        // Arrange: latest is 1 day old, one release is 20 days old (outside 7-day window of latest)
        var package = await CreatePackage();
        var latestDate = DateTimeOffset.UtcNow.AddDays(-1);
        await AddRelease(package.Id, "v1.0.0", "Very old", "Old features", latestDate.AddDays(-20));
        await AddRelease(package.Id, "v1.1.0", "Recent", "New features", latestDate);

        IReadOnlyList<ReleaseInput>? capturedReleases = null;
        _mockAiClient
            .Setup(x => x.SummarizeReleaseNotesAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ReleaseInput>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<ReleaseInput>, CancellationToken>((_, releases, _) =>
                capturedReleases = releases)
            .ReturnsAsync("Summary");

        // Act
        await _service.GenerateGroupSummariesAsync(package.Id);

        // Assert: only the recent release should be included
        capturedReleases.Should().NotBeNull();
        capturedReleases.Should().ContainSingle();
        capturedReleases![0].Tag.Should().Be("v1.1.0");
    }

    #endregion

    #region Helper Methods

    private async Task<Package> CreatePackage(string name = "test-pkg")
    {
        var package = new Package
        {
            Name = name,
            Url = $"https://github.com/owner/{name}",
            NpmName = name,
            GithubOwner = "owner",
            GithubRepo = name
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();
        return package;
    }

    private async Task<Release> AddRelease(
        string packageId, string tag, string? title = null, string? body = null,
        DateTimeOffset? publishedAt = null)
    {
        var parsed = VersionParser.ParseTagValues(tag);
        var release = new Release
        {
            PackageId = packageId,
            Tag = tag,
            Title = title,
            Body = body,
            PublishedAt = publishedAt ?? DateTimeOffset.UtcNow,
            FetchedAt = DateTimeOffset.UtcNow,
            SummaryStale = true,
            MajorVersion = parsed.MajorVersion,
            MinorVersion = parsed.MinorVersion,
            PatchVersion = parsed.PatchVersion,
            IsPrerelease = parsed.IsPrerelease
        };
        _db.Releases.Add(release);
        await _db.SaveChangesAsync();
        return release;
    }

    #endregion
}
