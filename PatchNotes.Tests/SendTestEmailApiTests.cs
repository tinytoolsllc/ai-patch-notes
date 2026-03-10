using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class SendTestEmailApiTests : IAsyncLifetime
{
    private const string EmailFunctionUrl = "http://fake-email-function/api/sendTestEmail";

    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _authClient = null!;
    private HttpClient _unauthClient = null!;
    private HttpClient _nonAdminClient = null!;
    private string _welcomeTemplateId = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        _fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("EmailFunction:Url", EmailFunctionUrl);
            builder.UseSetting("EmailFunction:Key", "test-function-key");
        });
        await _fixture.InitializeAsync();
        _authClient = _fixture.CreateAuthenticatedClient();
        _unauthClient = _fixture.CreateClient();
        _nonAdminClient = _fixture.CreateNonAdminClient();

        // Seed a test template directly in the database
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var template = new EmailTemplate
        {
            Name = "welcome",
            Subject = "Welcome, {{name}}!",
            JsxSource = "<div>Welcome</div>",
        };
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync();
        _welcomeTemplateId = template.Id;
    }

    public Task DisposeAsync()
    {
        _authClient.Dispose();
        _unauthClient.Dispose();
        _nonAdminClient.Dispose();
        _fixture.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SendTestEmail_Returns403_WhenUnauthenticated()
    {
        // CSRF middleware rejects requests without Origin header before auth runs
        var response = await _unauthClient.PostAsJsonAsync(
            $"/api/admin/email-templates/{_welcomeTemplateId}/test",
            new { recipientEmail = "a@b.com", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SendTestEmail_Returns403_WhenNonAdmin()
    {
        var response = await _nonAdminClient.PostAsJsonAsync(
            $"/api/admin/email-templates/{_welcomeTemplateId}/test",
            new { recipientEmail = "a@b.com", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SendTestEmail_Returns404_WhenTemplateNotFound()
    {
        _fixture.NpmHandler.SetupResponse(EmailFunctionUrl, HttpStatusCode.OK, """{"success":true}""");

        var response = await _authClient.PostAsJsonAsync(
            "/api/admin/email-templates/nonexistent-id/test",
            new { recipientEmail = "a@b.com", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SendTestEmail_Returns204_WhenUpstreamSucceeds()
    {
        _fixture.NpmHandler.SetupResponse(EmailFunctionUrl, HttpStatusCode.OK, """{"success":true}""");

        var response = await _authClient.PostAsJsonAsync(
            $"/api/admin/email-templates/{_welcomeTemplateId}/test",
            new { recipientEmail = "admin@test.com", testData = new { name = "Jane" } });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SendTestEmail_PassesThroughUpstreamError()
    {
        _fixture.NpmHandler.SetupResponse(EmailFunctionUrl, HttpStatusCode.InternalServerError, "render failed");

        var response = await _authClient.PostAsJsonAsync(
            $"/api/admin/email-templates/{_welcomeTemplateId}/test",
            new { recipientEmail = "admin@test.com", testData = new { name = "Jane" } });
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Email function error");
    }

    [Fact]
    public async Task SendTestEmail_Returns503_WhenEmailFunctionUrlNotConfigured()
    {
        // Create a new fixture without EmailFunction:Url configured
        using var fixture = new PatchNotesApiFixture();
        await fixture.InitializeAsync();
        using var client = fixture.CreateAuthenticatedClient();

        var templateId = await SeedTemplateAsync(fixture);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/email-templates/{templateId}/test",
            new { recipientEmail = "a@b.com", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task SendTestEmail_Returns400_WhenRecipientEmailMissing()
    {
        var response = await _authClient.PostAsJsonAsync(
            $"/api/admin/email-templates/{_welcomeTemplateId}/test",
            new { recipientEmail = "", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendTestEmail_Returns503_WhenEmailFunctionKeyNotConfigured()
    {
        // Create a fixture with URL but no Key
        using var fixture = new PatchNotesApiFixture();
        fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("EmailFunction:Url", EmailFunctionUrl);
        });
        await fixture.InitializeAsync();
        using var client = fixture.CreateAuthenticatedClient();

        var templateId = await SeedTemplateAsync(fixture);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/email-templates/{templateId}/test",
            new { recipientEmail = "a@b.com", testData = new { name = "Test" } });
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<string> SeedTemplateAsync(PatchNotesApiFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var template = new EmailTemplate
        {
            Name = "welcome",
            Subject = "Welcome, {{name}}!",
            JsxSource = "<div>Welcome</div>",
        };
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }
}
