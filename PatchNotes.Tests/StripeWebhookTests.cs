using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PatchNotes.Data;
using Stripe;

namespace PatchNotes.Tests;

public class StripeWebhookTests : IAsyncLifetime
{
    private const string TestWebhookSecret = "whsec_testsecretvalueforsigning";
    private const string KnownCustomerId = "cus_known123";

    private PatchNotesApiFixture _fixture = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _fixture = new PatchNotesApiFixture();
        _fixture.ConfigureSettings(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", TestWebhookSecret);
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

    #region Event ownership

    [Fact]
    public async Task HandleWebhook_GivenInvoiceForKnownCustomer_IsProcessed()
    {
        // Invoices never carry our "app" metadata — Stripe does not propagate it from the
        // Checkout Session or the subscription. They have to be recognised by their customer,
        // or every renewal and payment failure is silently dropped.
        await SeedUserAsync(KnownCustomerId, status: "active");

        var response = await PostEventAsync("invoice.payment_failed", $$"""
            {
              "id": "in_test1",
              "object": "invoice",
              "customer": "{{KnownCustomerId}}",
              "metadata": {}
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("ignored");
        (await GetUserAsync(KnownCustomerId))!.SubscriptionStatus.Should().Be("past_due");
    }

    [Fact]
    public async Task HandleWebhook_GivenInvoiceForUnknownCustomer_IsIgnored()
    {
        await SeedUserAsync(KnownCustomerId, status: "active");

        var response = await PostEventAsync("invoice.payment_failed", """
            {
              "id": "in_test2",
              "object": "invoice",
              "customer": "cus_someoneelse",
              "metadata": {}
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ignored");
        (await GetUserAsync(KnownCustomerId))!.SubscriptionStatus.Should().Be("active");

        // An ignored event is not recorded, so it stays eligible if we later learn the customer.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.ProcessedWebhookEvents.Count().Should().Be(0);
    }

    [Fact]
    public async Task HandleWebhook_GivenAppMetadataButUnknownCustomer_IsProcessed()
    {
        // The metadata path has to keep working on its own: a checkout session arrives before
        // we have stored a customer id for that user.
        await SeedUserAsync(KnownCustomerId, status: "active");

        var response = await PostEventAsync("invoice.payment_failed", $$"""
            {
              "id": "in_test3",
              "object": "invoice",
              "customer": "{{KnownCustomerId}}",
              "metadata": { "app": "patchnotes" }
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("ignored");
    }

    [Fact]
    public async Task HandleWebhook_GivenForeignAppMetadata_IsIgnored()
    {
        var response = await PostEventAsync("invoice.payment_failed", """
            {
              "id": "in_test4",
              "object": "invoice",
              "customer": "cus_othertenant",
              "metadata": { "app": "someoneelse" }
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ignored");
    }

    #endregion

    #region Stale and superseded events

    [Fact]
    public async Task HandleWebhook_GivenDeleteForSupersededSubscription_LeavesUserAlone()
    {
        // A delayed "deleted" for a subscription the user already replaced must not cancel the
        // current one. Re-fetching cannot catch this: the old subscription really is cancelled.
        await SeedUserAsync(KnownCustomerId, status: "active", subscriptionId: "sub_current");

        var response = await PostEventAsync("customer.subscription.deleted", $$"""
            {
              "id": "sub_old",
              "object": "subscription",
              "customer": "{{KnownCustomerId}}",
              "metadata": {},
              "items": { "object": "list", "data": [] }
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserAsync(KnownCustomerId);
        user!.SubscriptionStatus.Should().Be("active");
        user.StripeSubscriptionId.Should().Be("sub_current");

        // Recorded, not ignored: the event was recognised as ours and deliberately declined.
        // Without this the test would also pass on an implementation that drops every
        // subscription event before it reaches a handler.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.ProcessedWebhookEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task HandleWebhook_GivenSessionWithoutMetadata_IsHandledNotCrashed()
    {
        // Recognising events by customer means a session with no metadata at all now reaches the
        // handler, where the old filter would have dropped it first. Reading metadata there has
        // to tolerate its absence, or Stripe retries a 500 until the endpoint is disabled.
        await SeedUserAsync(KnownCustomerId, status: "active");

        var response = await PostEventAsync("checkout.session.completed", $$"""
            {
              "id": "cs_nometa",
              "object": "checkout.session",
              "customer": "{{KnownCustomerId}}"
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync(KnownCustomerId))!.SubscriptionStatus.Should().Be("active");
    }

    #endregion

    #region Transport

    [Fact]
    public async Task HandleWebhook_GivenInvalidSignature_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", "t=123,v1=deadbeef");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleWebhook_GivenDuplicateEventId_ReturnsDuplicateFlag()
    {
        await SeedUserAsync(KnownCustomerId, status: "active");
        var data = $$"""
            {
              "id": "in_dup",
              "object": "invoice",
              "customer": "{{KnownCustomerId}}",
              "metadata": {}
            }
            """;

        var first = await PostEventAsync("invoice.payment_failed", data, eventId: "evt_dup");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await PostEventAsync("invoice.payment_failed", data, eventId: "evt_dup");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("duplicate");
    }

    #endregion

    #region Helpers

    private Task<HttpResponseMessage> PostEventAsync(
        string type, string dataObject, string? eventId = null)
    {
        // api_version has to match the SDK's, because ConstructEvent is called with
        // throwOnApiVersionMismatch: true. Reading it at runtime keeps these tests working
        // across SDK upgrades instead of pinning a version string that goes stale.
        var body = $$"""
            {
              "id": "{{eventId ?? "evt_" + Guid.NewGuid().ToString("N")[..16]}}",
              "object": "event",
              "api_version": "{{StripeConfiguration.ApiVersion}}",
              "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
              "type": "{{type}}",
              "livemode": false,
              "pending_webhooks": 0,
              "request": { "id": null, "idempotency_key": null },
              "data": { "object": {{dataObject}} }
            }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", Sign(body));
        return _client.SendAsync(request);
    }

    private static string Sign(string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestWebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private async Task SeedUserAsync(
        string customerId, string status, string? subscriptionId = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.Users.Add(new User
        {
            StytchUserId = "stytch_" + customerId,
            Email = $"{customerId}@example.com",
            StripeCustomerId = customerId,
            StripeSubscriptionId = subscriptionId,
            SubscriptionStatus = status,
        });
        await db.SaveChangesAsync();
    }

    private async Task<User?> GetUserAsync(string customerId)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        return await Task.FromResult(
            db.Users.FirstOrDefault(u => u.StripeCustomerId == customerId));
    }

    #endregion
}
