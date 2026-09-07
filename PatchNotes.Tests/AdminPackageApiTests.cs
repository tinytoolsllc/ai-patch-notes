using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class AdminPackageApiTests : IAsyncLifetime
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

    #region POST /api/packages

    [Fact]
    public async Task CreatePackage_GivenUnauthenticatedRequest_ReturnsUnauthorized()
    {
        // The Origin header is sent explicitly because CsrfMiddleware runs before authentication:
        // without it the request is rejected as CSRF and never reaches the auth filter, so this
        // would pass for the wrong reason.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages")
        {
            Content = JsonContent.Create(new { owner = "facebook", repo = "react" }),
        };
        request.Headers.Add("Origin", "http://localhost");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreatePackage_GivenNoOriginHeader_IsRejectedAsCsrfBeforeAuth()
    {
        // Documents the ordering: an unauthenticated POST with no Origin is refused by
        // CsrfMiddleware, not by the auth filter. Anything driving this API from outside a browser
        // has to send Origin on every mutating request.
        var response = await _client.PostAsJsonAsync("/api/packages",
            new { owner = "facebook", repo = "react" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePackage_GivenNonAdminRequest_ReturnsForbidden()
    {
        var response = await _nonAdminClient.PostAsJsonAsync("/api/packages",
            new { owner = "facebook", repo = "react" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePackage_CreatesATrackedPackageReadyForSync()
    {
        var response = await _authClient.PostAsJsonAsync("/api/packages",
            new { owner = "facebook", repo = "react" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("react");
        body.GetProperty("url").GetString().Should().Be("https://github.com/facebook/react");
        body.GetProperty("githubOwner").GetString().Should().Be("facebook");

        // A null LastFetchedAt is how the sync job recognises a package it has never seen.
        body.GetProperty("lastFetchedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreatePackage_GivenOptionalFields_StoresThem()
    {
        var response = await _authClient.PostAsJsonAsync("/api/packages",
            new { owner = "vuejs", repo = "core", name = "Vue", npmName = "vue", tagPrefix = "v" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Vue");
        body.GetProperty("npmName").GetString().Should().Be("vue");
        body.GetProperty("tagPrefix").GetString().Should().Be("v");
    }

    [Fact]
    public async Task CreatePackage_GivenAnAlreadyTrackedRepo_ReturnsConflict()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
            db.Packages.Add(new Package
            {
                Name = "react",
                Url = "https://github.com/facebook/react",
                GithubOwner = "facebook",
                GithubRepo = "react",
            });
            await db.SaveChangesAsync();
        }

        var response = await _authClient.PostAsJsonAsync("/api/packages",
            new { owner = "facebook", repo = "react" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("", "react")]
    [InlineData("facebook", "")]
    [InlineData("has space", "react")]
    [InlineData("facebook", "../etc")]
    public async Task CreatePackage_GivenInvalidOwnerOrRepo_ReturnsBadRequest(string owner, string repo)
    {
        var response = await _authClient.PostAsJsonAsync("/api/packages",
            new { owner, repo });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

}
