using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class AdminWatchlistTemplateApiTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _authClient = null!;
    private HttpClient _nonAdminClient = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _authClient = _fixture.CreateAuthenticatedClient();
        _nonAdminClient = _fixture.CreateNonAdminClient();
    }

    public async Task DisposeAsync()
    {
        _authClient.Dispose();
        _nonAdminClient.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task CreateTemplate_GivenNonAdminRequest_ReturnsForbidden()
    {
        // The GET on this path stays public for onboarding; only the mutations are gated.
        var response = await _nonAdminClient.PostAsJsonAsync("/api/watchlist/templates",
            new { name = "Frontend", description = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetTemplates_StaysPublic()
    {
        var response = await _nonAdminClient.GetAsync("/api/watchlist/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #region Create

    [Fact]
    public async Task CreateTemplate_ReturnsCreatedWithNoPackages()
    {
        var response = await _authClient.PostAsJsonAsync("/api/watchlist/templates",
            new { name = "Frontend", description = "React and friends", sortOrder = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Frontend");
        body.GetProperty("sortOrder").GetInt32().Should().Be(2);
        body.GetProperty("packageCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task CreateTemplate_GivenBlankName_ReturnsBadRequest()
    {
        var response = await _authClient.PostAsJsonAsync("/api/watchlist/templates",
            new { name = "   ", description = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTemplate_GivenDuplicateName_ReturnsConflict()
    {
        await SeedTemplateAsync("Frontend");

        var response = await _authClient.PostAsJsonAsync("/api/watchlist/templates",
            new { name = "Frontend", description = "again" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Update

    [Fact]
    public async Task UpdateTemplate_GivenOnlySortOrder_LeavesOtherFieldsAlone()
    {
        // A partial update must not blank the fields it does not mention.
        var id = await SeedTemplateAsync("Frontend", "The original description", sortOrder: 1);

        var response = await _authClient.PatchAsJsonAsync(
            $"/api/watchlist/templates/{id}", new { sortOrder = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sortOrder").GetInt32().Should().Be(9);
        body.GetProperty("name").GetString().Should().Be("Frontend");
        body.GetProperty("description").GetString().Should().Be("The original description");
    }

    [Fact]
    public async Task UpdateTemplate_GivenNameTakenByAnother_ReturnsConflict()
    {
        await SeedTemplateAsync("Frontend");
        var id = await SeedTemplateAsync("Backend");

        var response = await _authClient.PatchAsJsonAsync(
            $"/api/watchlist/templates/{id}", new { name = "Frontend" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateTemplate_GivenItsOwnName_IsAllowed()
    {
        var id = await SeedTemplateAsync("Frontend");

        var response = await _authClient.PatchAsJsonAsync(
            $"/api/watchlist/templates/{id}", new { name = "Frontend", sortOrder = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateTemplate_GivenUnknownId_ReturnsNotFound()
    {
        var response = await _authClient.PatchAsJsonAsync(
            "/api/watchlist/templates/aaaaaaaaaaaaaaaaaaaaa", new { sortOrder = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delete

    [Fact]
    public async Task DeleteTemplate_RemovesMembershipButKeepsThePackages()
    {
        // A template is a curated list, not an owner. Deleting it must not delete tracked packages.
        var id = await SeedTemplateAsync("Frontend");
        var packageId = await SeedPackageAsync("react");
        await SetPackagesAsync(id, packageId);

        var response = await _authClient.DeleteAsync($"/api/watchlist/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.WatchlistTemplates.Count().Should().Be(0);
        db.WatchlistTemplatePackages.Count().Should().Be(0);
        db.Packages.Count().Should().Be(1);
    }

    [Fact]
    public async Task DeleteTemplate_GivenUnknownId_ReturnsNotFound()
    {
        var response = await _authClient.DeleteAsync(
            "/api/watchlist/templates/aaaaaaaaaaaaaaaaaaaaa");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Set packages

    [Fact]
    public async Task SetPackages_ReplacesTheMembershipWholesale()
    {
        var id = await SeedTemplateAsync("Frontend");
        var reactId = await SeedPackageAsync("react");
        var vueId = await SeedPackageAsync("vue");
        await SetPackagesAsync(id, reactId);

        var response = await _authClient.PutAsJsonAsync(
            $"/api/watchlist/templates/{id}/packages", new { packageIds = new[] { vueId } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("packageCount").GetInt32().Should().Be(1);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.WatchlistTemplatePackages.Single().PackageId.Should().Be(vueId);
    }

    [Fact]
    public async Task SetPackages_GivenAnUnknownId_ChangesNothing()
    {
        // Validation happens before any write, so a typo cannot leave the template half-updated.
        var id = await SeedTemplateAsync("Frontend");
        var reactId = await SeedPackageAsync("react");
        await SetPackagesAsync(id, reactId);

        var response = await _authClient.PutAsJsonAsync(
            $"/api/watchlist/templates/{id}/packages",
            new { packageIds = new[] { reactId, "aaaaaaaaaaaaaaaaaaaaa" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.WatchlistTemplatePackages.Single().PackageId.Should().Be(reactId);
    }

    [Fact]
    public async Task SetPackages_GivenAnEmptyList_ClearsTheTemplate()
    {
        var id = await SeedTemplateAsync("Frontend");
        var reactId = await SeedPackageAsync("react");
        await SetPackagesAsync(id, reactId);

        var response = await _authClient.PutAsJsonAsync(
            $"/api/watchlist/templates/{id}/packages", new { packageIds = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.WatchlistTemplatePackages.Count().Should().Be(0);
    }

    [Fact]
    public async Task SetPackages_DeduplicatesRepeatedIds()
    {
        var id = await SeedTemplateAsync("Frontend");
        var reactId = await SeedPackageAsync("react");

        var response = await _authClient.PutAsJsonAsync(
            $"/api/watchlist/templates/{id}/packages",
            new { packageIds = new[] { reactId, reactId } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("packageCount").GetInt32().Should().Be(1);
    }

    #endregion

    #region Helpers

    private async Task<string> SeedTemplateAsync(
        string name, string description = "desc", int sortOrder = 0)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var template = new WatchlistTemplate
        {
            Name = name,
            Description = description,
            SortOrder = sortOrder,
        };
        db.WatchlistTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    private async Task<string> SeedPackageAsync(string name)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var package = new Package
        {
            Name = name,
            Url = $"https://github.com/owner/{name}",
            GithubOwner = "owner",
            GithubRepo = name,
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package.Id;
    }

    private async Task SetPackagesAsync(string templateId, params string[] packageIds)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        foreach (var packageId in packageIds)
        {
            db.WatchlistTemplatePackages.Add(new WatchlistTemplatePackage
            {
                WatchlistTemplateId = templateId,
                PackageId = packageId,
            });
        }
        await db.SaveChangesAsync();
    }

    #endregion
}
