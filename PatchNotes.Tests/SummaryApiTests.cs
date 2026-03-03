using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class SummaryApiTests : IAsyncLifetime
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

    #region GET /api/summaries

    [Fact]
    public async Task GetSummaries_ReturnsOk_WhenNoSummaries()
    {
        var response = await _client.GetAsync("/api/summaries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetSummaries_ReturnsSummaries_WhenSummariesExist()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "react", Url = "https://github.com/facebook/react", NpmName = "react", GithubOwner = "facebook", GithubRepo = "react" };
        db.Packages.Add(pkg);
        db.ReleaseSummaries.AddRange(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 18, IsPrerelease = false, Summary = "React 18 summary", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 17, IsPrerelease = false, Summary = "React 17 summary", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/summaries");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetSummaries_GivenLimit_ReturnsCorrectCount()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "vue", Url = "https://github.com/vuejs/core", NpmName = "vue", GithubOwner = "vuejs", GithubRepo = "core" };
        db.Packages.Add(pkg);
        db.ReleaseSummaries.AddRange(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 3, IsPrerelease = false, Summary = "Vue 3", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 2, IsPrerelease = false, Summary = "Vue 2", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 1, IsPrerelease = false, Summary = "Vue 1", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/summaries?limit=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetSummaries_GivenLimitOfZero_ClampsToMin1()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "lodash", Url = "https://github.com/lodash/lodash", NpmName = "lodash", GithubOwner = "lodash", GithubRepo = "lodash" };
        db.Packages.Add(pkg);
        db.ReleaseSummaries.AddRange(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 4, IsPrerelease = false, Summary = "Lodash 4", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 3, IsPrerelease = false, Summary = "Lodash 3", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/summaries?limit=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetSummaries_GivenNegativeLimit_ClampsToMin1()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var pkg = new Package { Name = "axios", Url = "https://github.com/axios/axios", NpmName = "axios", GithubOwner = "axios", GithubRepo = "axios" };
        db.Packages.Add(pkg);
        db.ReleaseSummaries.AddRange(
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 2, IsPrerelease = false, Summary = "Axios 2", GeneratedAt = DateTimeOffset.UtcNow },
            new ReleaseSummary { PackageId = pkg.Id, MajorVersion = 1, IsPrerelease = false, Summary = "Axios 1", GeneratedAt = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/summaries?limit=-5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetArrayLength().Should().Be(1);
    }

    #endregion
}
