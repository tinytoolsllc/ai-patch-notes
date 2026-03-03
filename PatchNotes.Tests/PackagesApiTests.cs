using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class PackagesApiTests : IAsyncLifetime
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

    #region GET /api/packages

    [Fact]
    public async Task GetPackages_ReturnsEmptyList_WhenNoPackages()
    {
        var response = await _client.GetAsync("/api/packages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(0);
        result.GetProperty("total").GetInt32().Should().Be(0);
        result.GetProperty("limit").GetInt32().Should().Be(20);
        result.GetProperty("offset").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetPackages_GivenPackagesExist_ReturnsAllPackages()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.AddRange(
            new Package { Name = "react", Url = "https://github.com/facebook/react", NpmName = "react", GithubOwner = "facebook", GithubRepo = "react" },
            new Package { Name = "vue", Url = "https://github.com/vuejs/core", NpmName = "vue", GithubOwner = "vuejs", GithubRepo = "core" }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(2);
        result.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetPackages_GivenPackagesExist_ReturnsCorrectFields()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var createdAt = DateTimeOffset.UtcNow;
        db.Packages.Add(new Package
        {
            Name = "lodash",
            Url = "https://github.com/lodash/lodash",
            NpmName = "lodash",
            GithubOwner = "lodash",
            GithubRepo = "lodash",
            LastFetchedAt = createdAt.AddHours(1)
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pkg = result.GetProperty("items")[0];
        pkg.GetProperty("npmName").GetString().Should().Be("lodash");
        pkg.GetProperty("githubOwner").GetString().Should().Be("lodash");
        pkg.GetProperty("githubRepo").GetString().Should().Be("lodash");
        pkg.TryGetProperty("id", out _).Should().BeTrue();
        pkg.TryGetProperty("createdAt", out _).Should().BeTrue();
        pkg.TryGetProperty("lastFetchedAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetPackages_GivenLimitAndOffset_PaginatesCorrectly()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.AddRange(
            new Package { Name = "alpha", Url = "https://github.com/o/alpha", NpmName = "alpha", GithubOwner = "o", GithubRepo = "alpha" },
            new Package { Name = "bravo", Url = "https://github.com/o/bravo", NpmName = "bravo", GithubOwner = "o", GithubRepo = "bravo" },
            new Package { Name = "charlie", Url = "https://github.com/o/charlie", NpmName = "charlie", GithubOwner = "o", GithubRepo = "charlie" }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages?limit=2&offset=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(2);
        result.GetProperty("total").GetInt32().Should().Be(3);
        result.GetProperty("limit").GetInt32().Should().Be(2);
        result.GetProperty("offset").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetPackages_GivenLimitExceeds100_ClampsToMax100()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package { Name = "pkg", Url = "https://github.com/o/r", NpmName = "pkg", GithubOwner = "o", GithubRepo = "r" });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages?limit=500");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("limit").GetInt32().Should().Be(100);
    }

    #endregion

    #region POST /api/packages

    [Fact]
    public async Task PostPackage_GivenUnauthenticatedRequest_Returns403()
    {
        var response = await _client.PostAsJsonAsync("/api/packages", new { npmName = "test" });

        // CSRF middleware rejects requests without Origin header before auth runs
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostPackage_ReturnsBadRequest_WhenNpmNameMissing()
    {
        var response = await _authClient.PostAsJsonAsync("/api/packages", new { npmName = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("npmName is required");
    }

    [Fact]
    public async Task PostPackage_ReturnsNotFound_WhenPackageNotOnNpm()
    {
        _fixture.NpmHandler.SetupPackageNotFound("nonexistent-package-xyz");

        var response = await _authClient.PostAsJsonAsync("/api/packages", new { npmName = "nonexistent-package-xyz" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Package not found on npm");
    }

    [Fact]
    public async Task PostPackage_ReturnsBadRequest_WhenPackageHasNoGitHubRepo()
    {
        _fixture.NpmHandler.SetupPackageWithoutRepo("package-without-repo");

        var response = await _authClient.PostAsJsonAsync("/api/packages", new { npmName = "package-without-repo" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Package does not have a GitHub repository");
    }

    [Fact]
    public async Task PostPackage_CreatesPackage_WhenValid()
    {
        _fixture.NpmHandler.SetupPackage("express", "expressjs", "express");

        var response = await _authClient.PostAsJsonAsync("/api/packages", new { npmName = "express" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pkg = await response.Content.ReadFromJsonAsync<JsonElement>();
        pkg.GetProperty("npmName").GetString().Should().Be("express");
        pkg.GetProperty("githubOwner").GetString().Should().Be("expressjs");
        pkg.GetProperty("githubRepo").GetString().Should().Be("express");
        pkg.TryGetProperty("id", out var id).Should().BeTrue();
        id.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostPackage_ReturnsConflict_WhenPackageAlreadyExists()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package
        {
            Name = "duplicate-pkg",
            Url = "https://github.com/owner/repo",
            NpmName = "duplicate-pkg",
            GithubOwner = "owner",
            GithubRepo = "repo",
        });
        await db.SaveChangesAsync();

        // Act
        _fixture.NpmHandler.SetupPackage("duplicate-pkg", "other", "repo");
        var response = await _authClient.PostAsJsonAsync("/api/packages", new { npmName = "duplicate-pkg" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Package already exists");
    }

    #endregion

    #region DELETE /api/packages/{id}

    [Fact]
    public async Task DeletePackage_GivenUnauthenticatedRequest_Returns403()
    {
        var response = await _client.DeleteAsync("/api/packages/nonexistent-id-1234xx");

        // CSRF middleware rejects requests without Origin header before auth runs
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeletePackage_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _authClient.DeleteAsync("/api/packages/nonexistent-id-1234xx");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletePackage_DeletesPackage_WhenExists()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package
        {
            Name = "to-delete",
            Url = "https://github.com/owner/repo",
            NpmName = "to-delete",
            GithubOwner = "owner",
            GithubRepo = "repo",
        };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();
        var id = pkg.Id;

        // Act
        var response = await _authClient.DeleteAsync($"/api/packages/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync("/api/packages");
        var result = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    #endregion

    #region GET /api/packages/{owner}

    [Fact]
    public async Task GetPackagesByOwner_ReturnsBadRequest_WhenOwnerTooLong()
    {
        var longOwner = new string('a', 129);
        var response = await _client.GetAsync($"/api/packages/{longOwner}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner name");
    }

    [Fact]
    public async Task GetPackagesByOwner_ReturnsBadRequest_WhenOwnerHasInvalidChars()
    {
        var response = await _client.GetAsync("/api/packages/owner@invalid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner name");
    }

    [Fact]
    public async Task GetPackagesByOwner_ReturnsEmptyList_WhenNoPackagesForOwner()
    {
        var response = await _client.GetAsync("/api/packages/nonexistent-owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(0);
        result.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetPackagesByOwner_GivenOwnerWithPackages_ReturnsMatchingPackages()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.AddRange(
            new Package { Name = ".NET Runtime", Url = "https://github.com/dotnet/runtime", NpmName = "dotnet-runtime", GithubOwner = "dotnet", GithubRepo = "runtime" },
            new Package { Name = "EF Core", Url = "https://github.com/dotnet/efcore", NpmName = "dotnet-efcore", GithubOwner = "dotnet", GithubRepo = "efcore" },
            new Package { Name = "React", Url = "https://github.com/facebook/react", NpmName = "react", GithubOwner = "facebook", GithubRepo = "react" }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/dotnet");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(2);
        result.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetPackagesByOwner_GivenOwnerWithPackages_ReturnsCorrectFields()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "FastAPI", Url = "https://github.com/tiangolo/fastapi", NpmName = "fastapi", GithubOwner = "tiangolo", GithubRepo = "fastapi" };
        db.Packages.Add(pkg);
        db.Releases.Add(new Release { PackageId = pkg.Id, Tag = "v0.100.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 0, MinorVersion = 100, PatchVersion = 0 });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/tiangolo");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var p = result.GetProperty("items")[0];
        p.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        p.GetProperty("name").GetString().Should().Be("FastAPI");
        p.GetProperty("githubOwner").GetString().Should().Be("tiangolo");
        p.GetProperty("githubRepo").GetString().Should().Be("fastapi");
        p.GetProperty("latestVersion").GetString().Should().Be("v0.100.0");
        p.TryGetProperty("lastUpdated", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetPackagesByOwner_GivenLimitAndOffset_PaginatesCorrectly()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.AddRange(
            new Package { Name = "alpha", Url = "https://github.com/testowner/alpha", NpmName = "alpha", GithubOwner = "testowner", GithubRepo = "alpha" },
            new Package { Name = "bravo", Url = "https://github.com/testowner/bravo", NpmName = "bravo", GithubOwner = "testowner", GithubRepo = "bravo" },
            new Package { Name = "charlie", Url = "https://github.com/testowner/charlie", NpmName = "charlie", GithubOwner = "testowner", GithubRepo = "charlie" }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/testowner?limit=1&offset=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items").GetArrayLength().Should().Be(1);
        result.GetProperty("total").GetInt32().Should().Be(3);
        result.GetProperty("limit").GetInt32().Should().Be(1);
        result.GetProperty("offset").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PostPackage_ReturnsForbidden_WhenNonAdmin()
    {
        _fixture.NpmHandler.SetupPackage("forbidden-pkg", "owner", "repo");

        var response = await _nonAdminClient.PostAsJsonAsync("/api/packages", new { npmName = "forbidden-pkg" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region PATCH /api/packages/{id}

    [Fact]
    public async Task PatchPackage_ReturnsForbidden_WhenNonAdmin()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "test", Url = "https://github.com/o/r", NpmName = "test", GithubOwner = "o", GithubRepo = "r" };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var response = await _nonAdminClient.PatchAsJsonAsync($"/api/packages/{pkg.Id}", new { githubOwner = "new-owner" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region DELETE /api/packages/{id}

    [Fact]
    public async Task DeletePackage_ReturnsForbidden_WhenNonAdmin()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "nodelete", Url = "https://github.com/o/r", NpmName = "nodelete", GithubOwner = "o", GithubRepo = "r" };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var response = await _nonAdminClient.DeleteAsync($"/api/packages/{pkg.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region GET /api/packages/{owner}/{repo}

    [Fact]
    public async Task GetPackageByOwnerRepo_ReturnsBadRequest_WhenOwnerTooLong()
    {
        var longOwner = new string('a', 129);
        var response = await _client.GetAsync($"/api/packages/{longOwner}/repo");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner or repo name");
    }

    [Fact]
    public async Task GetPackageByOwnerRepo_ReturnsBadRequest_WhenRepoTooLong()
    {
        var longRepo = new string('a', 129);
        var response = await _client.GetAsync($"/api/packages/owner/{longRepo}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner or repo name");
    }

    [Fact]
    public async Task GetPackageByOwnerRepo_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _client.GetAsync("/api/packages/nonexistent/repo");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Package not found");
    }

    [Fact]
    public async Task GetPackageByOwnerRepo_GivenPackageWithGroups_ReturnsPackageAndGroups()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = ".NET Runtime", Url = "https://github.com/dotnet/runtime", NpmName = "dotnet-runtime", GithubOwner = "dotnet", GithubRepo = "runtime" };
        db.Packages.Add(pkg);
        db.Releases.AddRange(
            new Release { PackageId = pkg.Id, Tag = "v9.0.0", Title = "v9.0.0", Body = "# Release notes for v9", PublishedAt = DateTimeOffset.UtcNow.AddDays(-10), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 9, MinorVersion = 0, PatchVersion = 0, IsPrerelease = false },
            new Release { PackageId = pkg.Id, Tag = "v9.0.1", Title = "v9.0.1", Body = "# Patch notes", PublishedAt = DateTimeOffset.UtcNow.AddDays(-5), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 9, MinorVersion = 0, PatchVersion = 1, IsPrerelease = false },
            new Release { PackageId = pkg.Id, Tag = "v10.0.0-preview.1", Title = "v10 Preview", Body = "# Preview notes", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 10, MinorVersion = 0, PatchVersion = 0, IsPrerelease = true }
        );
        db.ReleaseSummaries.Add(new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 9, IsPrerelease = false, Summary = "AI summary for v9", GeneratedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/dotnet/runtime");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Check package info
        var package = result.GetProperty("package");
        package.GetProperty("name").GetString().Should().Be(".NET Runtime");
        package.GetProperty("githubOwner").GetString().Should().Be("dotnet");
        package.GetProperty("githubRepo").GetString().Should().Be("runtime");

        // Check groups
        var groups = result.GetProperty("groups");
        groups.GetArrayLength().Should().Be(2); // v9 stable + v10 prerelease

        // Find the v9 stable group
        var v9Group = groups.EnumerateArray().First(g => g.GetProperty("majorVersion").GetInt32() == 9);
        v9Group.GetProperty("isPrerelease").GetBoolean().Should().BeFalse();
        v9Group.GetProperty("versionRange").GetString().Should().Be("v9.x");
        v9Group.GetProperty("summary").GetString().Should().Be("AI summary for v9");
        v9Group.GetProperty("releaseCount").GetInt32().Should().Be(2);
        v9Group.GetProperty("releases").GetArrayLength().Should().Be(2);

        // Check releases include body (full markdown)
        var firstRelease = v9Group.GetProperty("releases")[0];
        firstRelease.TryGetProperty("body", out var body).Should().BeTrue();
        body.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPackageByOwnerRepo_GivenMultipleHistoricGroups_ReturnsAllGroups()
    {
        // Arrange - multiple major versions including old ones
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "TestPkg", Url = "https://github.com/test/pkg", NpmName = "test-pkg", GithubOwner = "test", GithubRepo = "pkg" };
        db.Packages.Add(pkg);
        db.Releases.AddRange(
            new Release { PackageId = pkg.Id, Tag = "v7.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-365), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 7, IsPrerelease = false },
            new Release { PackageId = pkg.Id, Tag = "v8.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-180), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 8, IsPrerelease = false },
            new Release { PackageId = pkg.Id, Tag = "v9.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-30), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 9, IsPrerelease = false }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/test/pkg");

        // Assert - should return ALL version groups, not filtered like feed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("groups").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task GetPackageByOwnerRepo_GroupsWithoutSummary_HaveNullSummary()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "NoSummary", Url = "https://github.com/ns/pkg", NpmName = "ns-pkg", GithubOwner = "ns", GithubRepo = "pkg" };
        db.Packages.Add(pkg);
        db.Releases.Add(new Release { PackageId = pkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 1, IsPrerelease = false });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/packages/ns/pkg");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var group = result.GetProperty("groups")[0];
        group.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Null);
    }

    #endregion

    #region GET /api/admin/packages/health

    [Fact]
    public async Task GetPackagesHealth_GivenUnauthenticatedRequest_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/packages/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPackagesHealth_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.GetAsync("/api/admin/packages/health");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPackagesHealth_ReturnsAllPackagesWithHealthFields()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Packages.AddRange(
            new Package
            {
                Name = "healthy-pkg",
                Url = "https://github.com/o/healthy",
                GithubOwner = "o",
                GithubRepo = "healthy",
                ConsecutiveFailures = 0,
                IsSyncDisabled = false,
            },
            new Package
            {
                Name = "failing-pkg",
                Url = "https://github.com/o/failing",
                GithubOwner = "o",
                GithubRepo = "failing",
                ConsecutiveFailures = 3,
                LastFailureAt = now.AddHours(-1),
                LastFailureMessage = "rate limit exceeded",
                IsSyncDisabled = false,
            },
            new Package
            {
                Name = "disabled-pkg",
                Url = "https://github.com/o/disabled",
                GithubOwner = "o",
                GithubRepo = "disabled",
                ConsecutiveFailures = 10,
                LastFailureAt = now.AddDays(-1),
                LastFailureMessage = "persistent error",
                IsSyncDisabled = true,
            }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/admin/packages/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = result.EnumerateArray().ToList();
        items.Should().HaveCount(3);

        // Disabled packages should come first
        items[0].GetProperty("isSyncDisabled").GetBoolean().Should().BeTrue();
        items[0].GetProperty("name").GetString().Should().Be("disabled-pkg");
        items[0].GetProperty("consecutiveFailures").GetInt32().Should().Be(10);
        items[0].GetProperty("lastFailureMessage").GetString().Should().Be("persistent error");

        // Then by consecutiveFailures descending
        items[1].GetProperty("name").GetString().Should().Be("failing-pkg");
        items[1].GetProperty("consecutiveFailures").GetInt32().Should().Be(3);

        items[2].GetProperty("name").GetString().Should().Be("healthy-pkg");
        items[2].GetProperty("consecutiveFailures").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetPackagesHealth_ReturnsRequiredFields()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Packages.Add(new Package
        {
            Name = "test-pkg",
            Url = "https://github.com/owner/repo",
            GithubOwner = "owner",
            GithubRepo = "repo",
            LastFetchedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _authClient.GetAsync("/api/admin/packages/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pkg = items.EnumerateArray().First();
        pkg.TryGetProperty("id", out _).Should().BeTrue();
        pkg.TryGetProperty("name", out _).Should().BeTrue();
        pkg.TryGetProperty("githubOwner", out _).Should().BeTrue();
        pkg.TryGetProperty("githubRepo", out _).Should().BeTrue();
        pkg.TryGetProperty("consecutiveFailures", out _).Should().BeTrue();
        pkg.TryGetProperty("lastFailureAt", out _).Should().BeTrue();
        pkg.TryGetProperty("lastFailureMessage", out _).Should().BeTrue();
        pkg.TryGetProperty("isSyncDisabled", out _).Should().BeTrue();
        pkg.TryGetProperty("lastFetchedAt", out _).Should().BeTrue();
    }

    #endregion

    #region POST /api/admin/packages/{id}/reset-sync

    [Fact]
    public async Task ResetPackageSync_GivenUnauthenticatedRequest_ReturnsForbidden()
    {
        // CSRF middleware rejects requests without Origin header before auth runs
        var response = await _client.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetPackageSync_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetPackageSync_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _authClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetPackageSync_ResetsFailureTracking_WhenPackageExists()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package
        {
            Name = "broken-pkg",
            Url = "https://github.com/o/broken",
            GithubOwner = "o",
            GithubRepo = "broken",
            ConsecutiveFailures = 5,
            LastFailureAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastFailureMessage = "some error",
            IsSyncDisabled = true,
        };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();
        var id = pkg.Id;

        // Act
        var response = await _authClient.PostAsync($"/api/admin/packages/{id}/reset-sync", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the package was reset
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var updated = await verifyDb.Packages.FindAsync(id);
        updated!.ConsecutiveFailures.Should().Be(0);
        updated.IsSyncDisabled.Should().BeFalse();
        updated.LastFailureMessage.Should().BeNull();
    }

    #endregion

    #region POST /api/admin/packages/{id}/disable-sync

    [Fact]
    public async Task DisablePackageSync_GivenUnauthenticatedRequest_ReturnsForbidden()
    {
        // CSRF middleware rejects requests without Origin header before auth runs
        var response = await _client.PostAsync("/api/admin/packages/nonexistent-id-1234xx/disable-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DisablePackageSync_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/disable-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DisablePackageSync_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _authClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/disable-sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisablePackageSync_SetsIsSyncDisabled_WhenPackageExists()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package
        {
            Name = "active-pkg",
            Url = "https://github.com/o/active",
            GithubOwner = "o",
            GithubRepo = "active",
            IsSyncDisabled = false,
        };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();
        var id = pkg.Id;

        // Act
        var response = await _authClient.PostAsync($"/api/admin/packages/{id}/disable-sync", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the package was disabled
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var updated = await verifyDb.Packages.FindAsync(id);
        updated!.IsSyncDisabled.Should().BeTrue();
    }

    #endregion

    #region POST /api/admin/packages/{id}/reset-summaries

    [Fact]
    public async Task ResetSummaries_GivenUnauthenticatedRequest_ReturnsForbidden()
    {
        // CSRF middleware rejects requests without Origin header before auth runs
        var response = await _client.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-summaries", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetSummaries_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-summaries", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetSummaries_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _authClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-summaries", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetSummaries_MarksReleasesStaleAndDeletesSummaries()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package
        {
            Name = "summary-reset-pkg",
            Url = "https://github.com/o/sr",
            GithubOwner = "o",
            GithubRepo = "sr",
        };
        db.Packages.Add(pkg);
        db.Releases.AddRange(
            new Release { PackageId = pkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-10), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 1, SummaryStale = false },
            new Release { PackageId = pkg.Id, Tag = "v1.0.1", PublishedAt = DateTimeOffset.UtcNow.AddDays(-5), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 1, SummaryStale = false },
            new Release { PackageId = pkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 2, SummaryStale = false }
        );
        db.ReleaseSummaries.AddRange(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 1, IsPrerelease = false, Summary = "v1 summary", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 2, IsPrerelease = false, Summary = "v2 summary", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();
        var id = pkg.Id;

        // Act
        var response = await _authClient.PostAsync($"/api/admin/packages/{id}/reset-summaries", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        // All releases should now be stale
        var releases = verifyDb.Releases.Where(r => r.PackageId == id).ToList();
        releases.Should().HaveCount(3);
        releases.Should().AllSatisfy(r => r.SummaryStale.Should().BeTrue());

        // All summaries should be deleted
        var summaries = verifyDb.ReleaseSummaries.Where(s => s.PackageId == id).ToList();
        summaries.Should().BeEmpty();
    }

    #endregion

    #region POST /api/admin/packages/{id}/reset-releases

    [Fact]
    public async Task ResetReleases_GivenUnauthenticatedRequest_ReturnsForbidden()
    {
        // CSRF middleware rejects requests without Origin header before auth runs
        var response = await _client.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-releases", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetReleases_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-releases", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetReleases_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var response = await _authClient.PostAsync("/api/admin/packages/nonexistent-id-1234xx/reset-releases", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetReleases_DeletesReleasesAndSummariesAndClearsLastFetchedAt()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package
        {
            Name = "release-reset-pkg",
            Url = "https://github.com/o/rr",
            GithubOwner = "o",
            GithubRepo = "rr",
            LastFetchedAt = DateTimeOffset.UtcNow,
        };
        db.Packages.Add(pkg);
        db.Releases.AddRange(
            new Release { PackageId = pkg.Id, Tag = "v1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-10), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 1 },
            new Release { PackageId = pkg.Id, Tag = "v2.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), FetchedAt = DateTimeOffset.UtcNow, MajorVersion = 2 }
        );
        db.ReleaseSummaries.Add(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 1, IsPrerelease = false, Summary = "v1 summary", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();
        var id = pkg.Id;

        // Act
        var response = await _authClient.PostAsync($"/api/admin/packages/{id}/reset-releases", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        // All releases should be deleted
        var releases = verifyDb.Releases.Where(r => r.PackageId == id).ToList();
        releases.Should().BeEmpty();

        // All summaries should be deleted
        var summaries = verifyDb.ReleaseSummaries.Where(s => s.PackageId == id).ToList();
        summaries.Should().BeEmpty();

        // LastFetchedAt should be cleared
        var updated = await verifyDb.Packages.FindAsync(id);
        updated!.LastFetchedAt.Should().BeNull();

        // Package itself should still exist
        updated.Name.Should().Be("release-reset-pkg");
    }

    #endregion
}
