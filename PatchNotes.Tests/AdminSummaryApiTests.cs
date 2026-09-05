using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class AdminSummaryApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;
    private HttpClient _authClient = null!;
    private HttpClient _nonAdminClient = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
        _authClient = _fixture.CreateAuthenticatedClient();
        _nonAdminClient = _fixture.CreateNonAdminClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _authClient.Dispose();
        _nonAdminClient.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    #region Authorization

    [Fact]
    public async Task GetSummaryQueue_GivenUnauthenticatedRequest_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummaryQueue_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.GetAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAdminSummaries_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.GetAsync("/api/admin/summaries");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region GET /api/admin/summaries/queue

    [Fact]
    public async Task GetSummaryQueue_GivenNothingQueued_ReturnsEmpty()
    {
        var response = await _authClient.GetAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(0);
        result.GetProperty("total").GetInt32().Should().Be(0);
        result.GetProperty("oldestQueuedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSummaryQueue_GivenStaleRelease_ReportsItWithReasonAndAge()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now.AddDays(-2), stale: true,
                fetchedAt: now.AddDays(-2)));
        });

        var response = await _authClient.GetAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
        result.GetProperty("totalStaleReleases").GetInt32().Should().Be(1);

        var entry = result.GetProperty("items")[0];
        entry.GetProperty("packageName").GetString().Should().Be("react");
        entry.GetProperty("reason").GetString().Should().Be("stale-release");
        entry.GetProperty("staleReleaseCount").GetInt32().Should().Be(1);
        entry.GetProperty("outOfWindow").GetBoolean().Should().BeFalse();
        entry.GetProperty("queuedSince").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSummaryQueue_GivenStaleReleaseBehindTheWindow_FlagsItOutOfWindow()
    {
        // The stale release sits 30 days behind its own group's newest, far outside SummaryWindow,
        // so it can never reach the model. Regenerating would reproduce the existing text.
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("lodash");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now.AddDays(-30), stale: true,
                fetchedAt: now.AddDays(-30)));
            db.Releases.Add(NewRelease(package.Id, "v1.9.0", 1, now, stale: false, fetchedAt: now));
        });

        var response = await _authClient.GetAsync("/api/admin/summaries/queue");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("outOfWindowPackages").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("outOfWindow").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetSummaryQueue_GivenStaleReleaseInAnotherVersionGroup_DoesNotFlagOutOfWindow()
    {
        // Same 30-day gap as the previous test, but the newer release belongs to a different major
        // version. outOfWindow is measured within a version group, so this one is still live work.
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("vue");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now.AddDays(-30), stale: true,
                fetchedAt: now.AddDays(-30)));
            db.Releases.Add(NewRelease(package.Id, "v2.0.0", 2, now, stale: false, fetchedAt: now));
        });

        var response = await _authClient.GetAsync("/api/admin/summaries/queue");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("outOfWindowPackages").GetInt32().Should().Be(0);
        result.GetProperty("items")[0].GetProperty("outOfWindow").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetSummaryQueue_GivenEmptySummaryRow_QueuesThePackage()
    {
        // A failed generation leaves an empty row behind, and GenerateAllSummariesAsync picks the
        // package up again on that basis alone — with no stale release involved.
        await SeedAsync(db =>
        {
            var package = NewPackage("svelte");
            db.Packages.Add(package);
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id,
                MajorVersion = 1,
                IsPrerelease = false,
                Summary = "",
                GeneratedAt = DateTimeOffset.UtcNow,
            });
        });

        var response = await _authClient.GetAsync("/api/admin/summaries/queue");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);

        var entry = result.GetProperty("items")[0];
        entry.GetProperty("reason").GetString().Should().Be("empty-summary");
        entry.GetProperty("staleReleaseCount").GetInt32().Should().Be(0);
        entry.GetProperty("queuedSince").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSummaryQueue_GivenOutOfWindowOnly_FiltersToDrainableEntries()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var live = NewPackage("live-pkg");
            var drainable = NewPackage("drainable-pkg");
            db.Packages.AddRange(live, drainable);

            db.Releases.Add(NewRelease(live.Id, "v1.0.0", 1, now.AddDays(-1), stale: true,
                fetchedAt: now.AddDays(-1)));

            db.Releases.Add(NewRelease(drainable.Id, "v1.0.0", 1, now.AddDays(-30), stale: true,
                fetchedAt: now.AddDays(-30)));
            db.Releases.Add(NewRelease(drainable.Id, "v1.9.0", 1, now, stale: false, fetchedAt: now));
        });

        var response = await _authClient.GetAsync("/api/admin/summaries/queue?outOfWindowOnly=true");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("packageName").GetString()
            .Should().Be("drainable-pkg");

        // The unfiltered counters still describe the whole queue, not the filtered page.
        result.GetProperty("totalStaleReleases").GetInt32().Should().Be(2);
        result.GetProperty("outOfWindowPackages").GetInt32().Should().Be(1);
    }

    #endregion

    #region GET /api/admin/summaries

    [Fact]
    public async Task GetAdminSummaries_ReturnsOperationalMetadataWithoutTheText()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id,
                MajorVersion = 19,
                IsPrerelease = false,
                Summary = "A generated summary.",
                GeneratedAt = now.AddHours(-3),
            });
            db.Releases.Add(NewRelease(package.Id, "v19.1.0", 19, now, stale: true, fetchedAt: now));
        });

        var response = await _authClient.GetAsync("/api/admin/summaries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);

        var item = result.GetProperty("items")[0];
        item.GetProperty("packageName").GetString().Should().Be("react");
        item.GetProperty("majorVersion").GetInt32().Should().Be(19);
        item.GetProperty("hasSummary").GetBoolean().Should().BeTrue();
        item.GetProperty("summaryLength").GetInt32().Should().Be("A generated summary.".Length);
        item.GetProperty("staleReleaseCount").GetInt32().Should().Be(1);

        // The text itself is not returned; a page of these would be enormous and unhelpful.
        item.TryGetProperty("summary", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminSummaries_GivenEmptySummary_ReportsHasSummaryFalse()
    {
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id,
                MajorVersion = 19,
                IsPrerelease = false,
                Summary = "",
                GeneratedAt = DateTimeOffset.UtcNow,
            });
        });

        var response = await _authClient.GetAsync("/api/admin/summaries");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = result.GetProperty("items")[0];
        item.GetProperty("hasSummary").GetBoolean().Should().BeFalse();
        item.GetProperty("summaryLength").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetAdminSummaries_ClampsPageSizeToTheMaximum()
    {
        var response = await _authClient.GetAsync("/api/admin/summaries?limit=5000");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("limit").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task GetAdminSummaries_GivenPackageIdFilter_ReturnsOnlyThatPackage()
    {
        string wantedId = null!;
        await SeedAsync(db =>
        {
            var wanted = NewPackage("wanted");
            var other = NewPackage("other");
            db.Packages.AddRange(wanted, other);
            wantedId = wanted.Id;

            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = wanted.Id, MajorVersion = 1, IsPrerelease = false,
                Summary = "Wanted.", GeneratedAt = DateTimeOffset.UtcNow,
            });
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = other.Id, MajorVersion = 1, IsPrerelease = false,
                Summary = "Other.", GeneratedAt = DateTimeOffset.UtcNow,
            });
        });

        var response = await _authClient.GetAsync($"/api/admin/summaries?packageId={wantedId}");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("packageName").GetString().Should().Be("wanted");
    }

    #endregion

    #region DELETE /api/admin/summaries/queue

    [Fact]
    public async Task DrainQueue_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.DeleteAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DrainQueue_DefaultsToOutOfWindow_AndLeavesLiveWorkQueued()
    {
        // The out-of-window release cannot reach the model; the recent one is live work. Draining
        // must take the first and leave the second, or it would silently drop a real summary.
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now.AddDays(-30), stale: true,
                fetchedAt: now.AddDays(-30)));
            db.Releases.Add(NewRelease(package.Id, "v1.9.0", 1, now.AddDays(-1), stale: true,
                fetchedAt: now.AddDays(-1)));
        });

        var response = await _authClient.DeleteAsync("/api/admin/summaries/queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("scope").GetString().Should().Be("out-of-window");
        result.GetProperty("releasesCleared").GetInt32().Should().Be(1);

        var stale = await StaleTagsAsync();
        stale.Should().BeEquivalentTo(["v1.9.0"]);
    }

    [Fact]
    public async Task DrainQueue_GivenOutOfWindow_DoesNotTouchAnotherVersionGroup()
    {
        // Same 30-day gap, but across major versions. v1.0.0 is still the newest of its own group,
        // so it is live work and must survive the drain.
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("vue");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now.AddDays(-30), stale: true,
                fetchedAt: now.AddDays(-30)));
            db.Releases.Add(NewRelease(package.Id, "v2.0.0", 2, now, stale: false, fetchedAt: now));
        });

        var response = await _authClient.DeleteAsync("/api/admin/summaries/queue");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("releasesCleared").GetInt32().Should().Be(0);
        (await StaleTagsAsync()).Should().BeEquivalentTo(["v1.0.0"]);
    }

    [Fact]
    public async Task DrainQueue_GivenScopePackage_ClearsOnlyThatPackage()
    {
        var now = DateTimeOffset.UtcNow;
        string targetId = null!;
        await SeedAsync(db =>
        {
            var target = NewPackage("target");
            var other = NewPackage("other");
            db.Packages.AddRange(target, other);
            targetId = target.Id;

            db.Releases.Add(NewRelease(target.Id, "v1.0.0", 1, now, stale: true, fetchedAt: now));
            db.Releases.Add(NewRelease(other.Id, "v1.0.0", 1, now, stale: true, fetchedAt: now));
        });

        var response = await _authClient.DeleteAsync(
            $"/api/admin/summaries/queue?scope=package&packageId={targetId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("releasesCleared").GetInt32().Should().Be(1);

        // The other package is untouched, even though its release is equally stale.
        (await StaleTagsAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task DrainQueue_GivenScopePackageWithoutId_ReturnsBadRequest()
    {
        var response = await _authClient.DeleteAsync("/api/admin/summaries/queue?scope=package");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DrainQueue_GivenUnknownScope_ReturnsBadRequest()
    {
        var response = await _authClient.DeleteAsync("/api/admin/summaries/queue?scope=everything");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DrainQueue_GivenScopeAll_ClearsStaleAndRemovesEmptySummaries()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now, stale: true, fetchedAt: now));
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id, MajorVersion = 1, IsPrerelease = false,
                Summary = "", GeneratedAt = now,
            });
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id, MajorVersion = 2, IsPrerelease = false,
                Summary = "A real summary.", GeneratedAt = now,
            });
        });

        var response = await _authClient.DeleteAsync("/api/admin/summaries/queue?scope=all");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("releasesCleared").GetInt32().Should().Be(1);
        result.GetProperty("emptySummariesDeleted").GetInt32().Should().Be(1);

        (await StaleTagsAsync()).Should().BeEmpty();

        // Draining never deletes a real summary — only the empty rows left by failed runs.
        using var scope = _fixture.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db2.ReleaseSummaries.Count().Should().Be(1);
    }

    #endregion

    #region POST /api/admin/summaries/regenerate-all

    [Fact]
    public async Task RegenerateAll_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsync(
            "/api/admin/summaries/regenerate-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegenerateAll_WithoutConfirm_ChangesNothingAndReportsTheBlastRadius()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now, stale: false, fetchedAt: now));
            db.Releases.Add(NewRelease(package.Id, "v1.1.0", 1, now, stale: false, fetchedAt: now));
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id, MajorVersion = 1, IsPrerelease = false,
                Summary = "Existing summary.", GeneratedAt = now,
            });
        });

        var response = await _authClient.PostAsync("/api/admin/summaries/regenerate-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("confirmed").GetBoolean().Should().BeFalse();
        result.GetProperty("summariesDeleted").GetInt32().Should().Be(1);
        result.GetProperty("releasesMarkedStale").GetInt32().Should().Be(2);

        // Nothing actually happened.
        using var scope = _fixture.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db2.ReleaseSummaries.Count().Should().Be(1);
        (await StaleTagsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RegenerateAll_WithConfirm_DeletesSummariesAndQueuesEverything()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(db =>
        {
            var package = NewPackage("react");
            db.Packages.Add(package);
            db.Releases.Add(NewRelease(package.Id, "v1.0.0", 1, now, stale: false, fetchedAt: now));
            db.ReleaseSummaries.Add(new ReleaseSummary
            {
                PackageId = package.Id, MajorVersion = 1, IsPrerelease = false,
                Summary = "Existing summary.", GeneratedAt = now,
            });
        });

        var response = await _authClient.PostAsync(
            "/api/admin/summaries/regenerate-all?confirm=true", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        result.GetProperty("summariesDeleted").GetInt32().Should().Be(1);

        using var scope = _fixture.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db2.ReleaseSummaries.Count().Should().Be(0);
        (await StaleTagsAsync()).Should().BeEquivalentTo(["v1.0.0"]);
    }

    #endregion

    #region Helpers

    /// <summary>Tags of every release still flagged stale, for asserting what a drain left behind.</summary>
    private async Task<List<string>> StaleTagsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        return await Task.FromResult(
            db.Releases.Where(r => r.SummaryStale).Select(r => r.Tag).ToList());
    }

    private async Task SeedAsync(Action<PatchNotesDbContext> seed)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static Package NewPackage(string name) => new()
    {
        Name = name,
        Url = $"https://github.com/owner/{name}",
        NpmName = name,
        GithubOwner = "owner",
        GithubRepo = name,
    };

    private static Release NewRelease(
        string packageId, string tag, int majorVersion, DateTimeOffset publishedAt,
        bool stale, DateTimeOffset fetchedAt) => new()
    {
        PackageId = packageId,
        Tag = tag,
        PublishedAt = publishedAt,
        FetchedAt = fetchedAt,
        SummaryStale = stale,
        MajorVersion = majorVersion,
        IsPrerelease = false,
    };

    #endregion
}
