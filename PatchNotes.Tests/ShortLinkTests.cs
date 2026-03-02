using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class ShortLinkTests : IAsyncLifetime
{
    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;
    private string _packageId = null!;
    private string _releaseId = null!;
    private string _packageOwner = null!;
    private string _packageRepo = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();

        var package = new Package
        {
            Name = "react",
            Url = "https://github.com/facebook/react",
            GithubOwner = "facebook",
            GithubRepo = "react",
        };
        db.Packages.Add(package);

        var release = new Release
        {
            PackageId = package.Id,
            Tag = "v19.0.0",
            PublishedAt = DateTimeOffset.UtcNow,
            FetchedAt = DateTimeOffset.UtcNow,
        };
        db.Releases.Add(release);
        await db.SaveChangesAsync();

        _packageId = package.Id;
        _releaseId = release.Id;
        _packageOwner = package.GithubOwner;
        _packageRepo = package.GithubRepo;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    [Fact]
    public async Task ShortLink_GivenReleaseId_Returns301ToReleasePage()
    {
        var response = await _client.GetAsync($"/s/{_releaseId}");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().Should().Be($"/releases/{_releaseId}");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(86400));
    }

    [Fact]
    public async Task ShortLink_GivenPackageId_Returns301ToPackagePage()
    {
        var response = await _client.GetAsync($"/s/{_packageId}");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().Should().Be($"/packages/{_packageOwner}/{_packageRepo}");
        response.Headers.CacheControl!.Public.Should().BeTrue();
    }

    [Fact]
    public async Task ShortLink_GivenUnknownId_Returns404()
    {
        var response = await _client.GetAsync("/s/nonexistent-id-12345");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
