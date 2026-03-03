using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;

namespace PatchNotes.Tests;

public class StytchWebhookTests : IAsyncLifetime
{
    // Valid Svix secret format: whsec_ + base64-encoded bytes
    private const string TestWebhookSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private readonly byte[] _secretBytes = Convert.FromBase64String("MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw");

    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        _fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("Stytch:WebhookSecret", TestWebhookSecret);
        });
        await _fixture.InitializeAsync();
        _client = _fixture.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _fixture.DisposeAsync();
        _fixture.Dispose();
    }

    private (string id, string timestamp, string signature) SignPayload(string body)
    {
        var msgId = "msg_" + Guid.NewGuid().ToString("N")[..20];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var toSign = $"{msgId}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(_secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var sig = "v1," + Convert.ToBase64String(hash);
        return (msgId, timestamp, sig);
    }

    private HttpRequestMessage CreateWebhookRequest(string body, string? svixId = null, string? svixTimestamp = null, string? svixSignature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stytch")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (svixId != null) request.Headers.TryAddWithoutValidation("svix-id", svixId);
        if (svixTimestamp != null) request.Headers.TryAddWithoutValidation("svix-timestamp", svixTimestamp);
        if (svixSignature != null) request.Headers.TryAddWithoutValidation("svix-signature", svixSignature);
        return request;
    }

    [Fact]
    public void AppStartup_GivenWebhookSecretMissing_ThrowsOnStartup()
    {
        // The app should refuse to start when Stytch:WebhookSecret is not configured,
        // preventing unverified webhook payloads from ever being processed.
        var fixture = new PatchNotesApiFixture();
        fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("Stytch:WebhookSecret", "");
        });

        var act = () => fixture.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Stytch:WebhookSecret*");

        fixture.Dispose();
    }

    [Fact]
    public async Task HandleWebhook_GivenSvixHeadersMissing_Returns401()
    {
        var body = """{"action":"CREATE","id":"user-test-123","object_type":"user"}""";
        var request = CreateWebhookRequest(body); // no svix headers

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HandleWebhook_GivenInvalidSignature_Returns401()
    {
        var body = """{"action":"CREATE","id":"user-test-123","object_type":"user"}""";
        var request = CreateWebhookRequest(body,
            svixId: "msg_test123",
            svixTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            svixSignature: "v1,invalidsignaturedata");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HandleWebhook_GivenValidUserCreateEvent_CreatesUser()
    {
        // Arrange
        var stytchUserId = PatchNotesApiFixture.TestUserId;
        var body = $$$"""{"action":"CREATE","id":"{{{stytchUserId}}}","object_type":"user"}""";
        var (id, ts, sig) = SignPayload(body);
        var request = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var user = db.Users.FirstOrDefault(u => u.StytchUserId == stytchUserId);
        user.Should().NotBeNull();
        user!.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task HandleWebhook_GivenValidUserUpdateEvent_UpdatesUser()
    {
        // Arrange: create user first
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
            db.Users.Add(new User
            {
                StytchUserId = PatchNotesApiFixture.TestUserId,
                Email = "old@example.com",
                Name = "Old Name",
            });
            await db.SaveChangesAsync();
        }

        var body = $$$"""{"action":"UPDATE","id":"{{{PatchNotesApiFixture.TestUserId}}}","object_type":"user"}""";
        var (id, ts, sig) = SignPayload(body);
        var request = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var readScope = _fixture.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var user = readDb.Users.FirstOrDefault(u => u.StytchUserId == PatchNotesApiFixture.TestUserId);
        user.Should().NotBeNull();
        // Mock returns "test@example.com" and "Test User" - should be updated from Stytch API
        user!.Email.Should().Be("test@example.com");
        user.Name.Should().Be("Test User");
    }

    [Fact]
    public async Task HandleWebhook_GivenValidUserDeleteEvent_DeletesUser()
    {
        // Arrange: create user first
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
            db.Users.Add(new User
            {
                StytchUserId = "user-to-delete",
                Email = "delete@example.com",
                Name = "Delete Me",
            });
            await db.SaveChangesAsync();
        }

        var body = """{"action":"DELETE","id":"user-to-delete","object_type":"user"}""";
        var (id, ts, sig) = SignPayload(body);
        var request = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var readScope = _fixture.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var user = readDb.Users.FirstOrDefault(u => u.StytchUserId == "user-to-delete");
        user.Should().BeNull();
    }

    [Fact]
    public async Task HandleWebhook_GivenUnhandledEventType_Returns200Ok()
    {
        var body = """{"action":"SOME_OTHER","id":"user-test-123","object_type":"organization"}""";
        var (id, ts, sig) = SignPayload(body);
        var request = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HandleWebhook_GivenDuplicateSvixId_ReturnsOkWithDuplicateFlag()
    {
        // Arrange: send the same webhook twice with the same svix-id
        var stytchUserId = PatchNotesApiFixture.TestUserId;
        var body = $$$"""{"action":"CREATE","id":"{{{stytchUserId}}}","object_type":"user"}""";
        var (id, ts, sig) = SignPayload(body);

        var firstRequest = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: send same svix-id again (simulating Svix retry)
        var secondRequest = CreateWebhookRequest(body, svixId: id, svixTimestamp: ts, svixSignature: sig);
        var secondResponse = await _client.SendAsync(secondRequest);

        // Assert: second delivery is acknowledged but flagged as duplicate
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await secondResponse.Content.ReadAsStringAsync();
        content.Should().Contain("duplicate");
    }
}
