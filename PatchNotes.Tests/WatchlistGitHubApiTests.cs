using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Api.Routes;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class WatchlistGitHubApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _authClient = null!;
    private HttpClient _nonAdminClient = null!;
    private HttpClient _unauthClient = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _authClient = _fixture.CreateAuthenticatedClient();
        _nonAdminClient = _fixture.CreateNonAdminClient();
        _unauthClient = _fixture.CreateClient();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Users.Add(new User
        {
            StytchUserId = PatchNotesApiFixture.TestUserId,
            Email = "test@example.com",
        });
        db.Users.Add(new User
        {
            StytchUserId = PatchNotesApiFixture.NonAdminUserId,
            Email = "nonadmin@example.com",
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _authClient.Dispose();
        _nonAdminClient.Dispose();
        _unauthClient.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task AddFromGitHub_GivenNewGitHubRepo_CreatesPackageAndAddsToWatchlist()
    {
        var response = await _authClient.PostAsync("/api/watchlist/github/facebook/react", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = result.GetProperty("packageId").GetString();
        packageId.Should().NotBeNullOrEmpty();

        // Verify it's in the watchlist
        var getResponse = await _authClient.GetAsync("/api/watchlist");
        var packages = await getResponse.Content.ReadFromJsonAsync<WatchlistPackageDto[]>();
        packages.Should().NotBeNull();
        packages!.Select(p => p.Id).Should().Contain(packageId);
    }

    [Fact]
    public async Task AddFromGitHub_UsesExistingPackage_WhenAlreadyInDb()
    {
        // Pre-create the package
        string existingPackageId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
            var pkg = new Package
            {
                Name = "vue",
                Url = "https://github.com/vuejs/core",
                GithubOwner = "vuejs",
                GithubRepo = "core",
            };
            db.Packages.Add(pkg);
            await db.SaveChangesAsync();
            existingPackageId = pkg.Id;
        }

        var response = await _authClient.PostAsync("/api/watchlist/github/vuejs/core", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("packageId").GetString().Should().Be(existingPackageId);
    }

    [Fact]
    public async Task AddFromGitHub_Returns409_WhenAlreadyWatching()
    {
        await _authClient.PostAsync("/api/watchlist/github/facebook/react", null);

        var response = await _authClient.PostAsync("/api/watchlist/github/facebook/react", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddFromGitHub_Returns403_WhenFreeTierLimitReached()
    {
        // Use non-admin client so the free tier limit applies
        // (admin users are treated as Pro and bypass the limit)
        for (int i = 0; i < 5; i++)
        {
            var res = await _nonAdminClient.PostAsync($"/api/watchlist/github/owner{i}/repo{i}", null);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 6th should be rejected
        var response = await _nonAdminClient.PostAsync("/api/watchlist/github/owner5/repo5", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddFromGitHub_GivenUnauthenticatedRequest_Returns403()
    {
        // POST without Origin header gets CSRF 403; with Origin but no session gets 401
        var response = await _unauthClient.PostAsync("/api/watchlist/github/facebook/react", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddFromGitHub_ReturnsBadRequest_WhenOwnerTooLong()
    {
        var longOwner = new string('a', 129);
        var response = await _authClient.PostAsync($"/api/watchlist/github/{longOwner}/repo", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner or repo name");
    }

    [Fact]
    public async Task AddFromGitHub_ReturnsBadRequest_WhenRepoTooLong()
    {
        var longRepo = new string('a', 129);
        var response = await _authClient.PostAsync($"/api/watchlist/github/owner/{longRepo}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("error").GetString().Should().Be("Invalid owner or repo name");
    }
}
