using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Api.Routes;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class WatchlistApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;
    private HttpClient _authClient = null!;
    private string _reactPackageId = null!;
    private string _vuePackageId = null!;
    private string _templateId = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
        _authClient = _fixture.CreateAuthenticatedClient();

        // Create test user and packages
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Users.Add(new User
        {
            StytchUserId = PatchNotesApiFixture.TestUserId,
            Email = "test@example.com",
        });
        var react = new Package { Name = "react", Url = "https://github.com/facebook/react", NpmName = "react", GithubOwner = "facebook", GithubRepo = "react" };
        var vue = new Package { Name = "vue", Url = "https://github.com/vuejs/core", NpmName = "vue", GithubOwner = "vuejs", GithubRepo = "core" };
        db.Packages.AddRange(react, vue);

        // Seed a watchlist template with both packages
        var template = new WatchlistTemplate { Name = "Frontend", Description = "React and Vue" };
        db.WatchlistTemplates.Add(template);
        await db.SaveChangesAsync();

        db.WatchlistTemplatePackages.AddRange(
            new WatchlistTemplatePackage { WatchlistTemplateId = template.Id, PackageId = react.Id },
            new WatchlistTemplatePackage { WatchlistTemplateId = template.Id, PackageId = vue.Id }
        );
        await db.SaveChangesAsync();

        _reactPackageId = react.Id;
        _vuePackageId = vue.Id;
        _templateId = template.Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _authClient.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task GetWatchlist_GivenUnauthenticatedRequest_Returns401()
    {
        var response = await _client.GetAsync("/api/watchlist");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWatchlist_ReturnsEmptyArray_ForNewUser()
    {
        var response = await _authClient.GetAsync("/api/watchlist");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var packages = await response.Content.ReadFromJsonAsync<WatchlistPackageDto[]>();
        packages.Should().BeEmpty();
    }

    [Fact]
    public async Task PutWatchlist_GivenValidPackageList_SetsWatchlist()
    {
        var response = await _authClient.PutAsJsonAsync("/api/watchlist", new { packageIds = new[] { _reactPackageId, _vuePackageId } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = await response.Content.ReadFromJsonAsync<string[]>();
        ids.Should().BeEquivalentTo([_reactPackageId, _vuePackageId]);

        // Verify GET returns same IDs (now as WatchlistPackageDto[])
        var getResponse = await _authClient.GetAsync("/api/watchlist");
        var getPackages = await getResponse.Content.ReadFromJsonAsync<WatchlistPackageDto[]>();
        getPackages.Should().NotBeNull();
        getPackages!.Select(p => p.Id).Should().BeEquivalentTo([_reactPackageId, _vuePackageId]);
    }

    [Fact]
    public async Task PutWatchlist_GivenExistingWatchlist_ReplacesItEntirely()
    {
        // Set initial
        await _authClient.PutAsJsonAsync("/api/watchlist", new { packageIds = new[] { _reactPackageId, _vuePackageId } });

        // Replace
        var response = await _authClient.PutAsJsonAsync("/api/watchlist", new { packageIds = new[] { _vuePackageId } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = await response.Content.ReadFromJsonAsync<string[]>();
        ids.Should().BeEquivalentTo([_vuePackageId]);
    }

    [Fact]
    public async Task PostWatchlist_GivenValidPackageId_AddsPackageToWatchlist()
    {
        var response = await _authClient.PostAsync($"/api/watchlist/{_reactPackageId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await _authClient.GetAsync("/api/watchlist");
        var packages = await getResponse.Content.ReadFromJsonAsync<WatchlistPackageDto[]>();
        packages.Should().NotBeNull();
        packages!.Select(p => p.Id).Should().Contain(_reactPackageId);
    }

    [Fact]
    public async Task PostWatchlist_Returns409_ForDuplicate()
    {
        await _authClient.PostAsync($"/api/watchlist/{_reactPackageId}", null);

        var response = await _authClient.PostAsync($"/api/watchlist/{_reactPackageId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Already watching this package");
    }

    [Fact]
    public async Task PostWatchlist_Returns404_ForNonexistentPackage()
    {
        var response = await _authClient.PostAsync("/api/watchlist/nonexistent-id", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWatchlist_GivenWatchedPackage_RemovesItFromWatchlist()
    {
        await _authClient.PostAsync($"/api/watchlist/{_reactPackageId}", null);
        await _authClient.PostAsync($"/api/watchlist/{_vuePackageId}", null);

        var response = await _authClient.DeleteAsync($"/api/watchlist/{_reactPackageId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _authClient.GetAsync("/api/watchlist");
        var packages = await getResponse.Content.ReadFromJsonAsync<WatchlistPackageDto[]>();
        packages.Should().NotBeNull();
        packages!.Select(p => p.Id).Should().BeEquivalentTo([_vuePackageId]);
    }

    [Fact]
    public async Task DeleteWatchlist_Returns204_EvenIfNotWatching()
    {
        var response = await _authClient.DeleteAsync($"/api/watchlist/{_reactPackageId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
