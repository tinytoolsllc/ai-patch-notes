using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PatchNotes.Api.Webhooks;
using PatchNotes.Data;
using Stripe;

namespace PatchNotes.Tests;

public class StripeWebhookTests : IAsyncLifetime
{
    private const string TestWebhookSecret = "whsec_testsecretvalueforsigning";
    private const string KnownCustomerId = "cus_known123";
    private const string CurrentSubscriptionId = "sub_current";
    private const long PeriodEndUnix = 1798761600; // 2027-01-01T00:00:00Z
    private static readonly DateTimeOffset PeriodEnd =
        DateTimeOffset.FromUnixTimeSeconds(PeriodEndUnix);

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

    #region Admission

    [Fact]
    public async Task HandleWebhook_GivenUntaggedInvoiceForCurrentSubscription_IsApplied()
    {
        // The original bug. Invoices never carry our "app" metadata, because Stripe propagates it
        // neither from the Checkout Session nor from the subscription, so dropping untagged events
        // silently discarded every renewal and payment failure.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("invoice.payment_failed", InvoiceJson());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"applied\":true");
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("past_due");
    }

    [Fact]
    public async Task HandleWebhook_GivenForeignAppTag_IsIgnoredBeforeAnyHandler()
    {
        // The only global check left, and the only one that is actual evidence: an explicit tag
        // naming a different app.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("invoice.payment_failed",
            InvoiceJson(metadata: """{ "app": "someoneelse" }"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ignored");
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("active");
    }

    [Fact]
    public async Task HandleWebhook_GivenInvoiceForUnknownCustomer_ChangesNothing()
    {
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("invoice.payment_failed",
            InvoiceJson(customerId: "cus_someoneelse"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"applied\":false");
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("active");
    }

    #endregion

    #region Recording only what was applied

    [Fact]
    public async Task HandleWebhook_GivenUnappliedEvent_IsNotRecordedSoItStaysReplayable()
    {
        // A row here means "never process this again". Claiming that for an event no handler acted
        // on burns it: one that arrives before its user is resolvable would be consumed on first
        // delivery and could never be replayed from the dashboard.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        await PostEventAsync("invoice.payment_failed", InvoiceJson(customerId: "cus_someoneelse"));

        (await CountRecordedAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleWebhook_GivenAppliedEvent_IsRecorded()
    {
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        await PostEventAsync("invoice.payment_failed", InvoiceJson());

        (await CountRecordedAsync()).Should().Be(1);
    }

    #endregion

    #region Invoices belong to a subscription

    [Fact]
    public async Task HandleWebhook_GivenInvoiceForReplacedSubscription_IsIgnored()
    {
        // Stripe retries a failed invoice for weeks. A customer who cancelled sub_old with an open
        // invoice and resubscribed would otherwise be flipped to past_due by every retry against
        // the dead subscription, reporting dunning for an account in good standing.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("invoice.payment_failed",
            InvoiceJson(subscriptionId: "sub_old"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("active");
        (await CountRecordedAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleWebhook_GivenInvoiceWithNoSubscription_IsIgnored()
    {
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("invoice.payment_failed",
            InvoiceJson(subscriptionId: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("active");
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task HandleWebhook_GivenDeleteForCurrentSubscription_KeepsThePaidPeriod()
    {
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("customer.subscription.deleted", SubscriptionJson());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserAsync();
        user!.SubscriptionStatus.Should().Be("canceled");
        user.SubscriptionExpiresAt.Should().Be(PeriodEnd);
    }

    [Fact]
    public async Task HandleWebhook_GivenDeleteWithNoPeriodEnd_KeepsTheExistingExpiry()
    {
        // IsPro reads a null expiry as "no paid period remaining", so overwriting unconditionally
        // would end a cancelled user's access the moment a payload came back without items.
        var existing = DateTimeOffset.UtcNow.AddDays(20);
        await SeedUserAsync(
            status: "active", subscriptionId: CurrentSubscriptionId, expiresAt: existing);

        await PostEventAsync("customer.subscription.deleted", SubscriptionJson(items: "[]"));

        var user = await GetUserAsync();
        user!.SubscriptionStatus.Should().Be("canceled");
        user.SubscriptionExpiresAt.Should().BeCloseTo(existing, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task HandleWebhook_GivenDeleteForSupersededSubscription_LeavesUserAlone()
    {
        // A delayed cancellation for a subscription the user already replaced must not cancel the
        // current one, and must stay replayable rather than being marked processed.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("customer.subscription.deleted",
            SubscriptionJson(subscriptionId: "sub_old"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await GetUserAsync();
        user!.SubscriptionStatus.Should().Be("active");
        user.StripeSubscriptionId.Should().Be(CurrentSubscriptionId);
        (await CountRecordedAsync()).Should().Be(0);
    }

    #endregion

    #region Checkout sessions

    [Fact]
    public async Task HandleWebhook_GivenSessionWithoutMetadata_IsHandledNotCrashed()
    {
        // Nothing upstream guarantees metadata, so the handler has to tolerate its absence.
        // Throwing here returns 500 and Stripe retries until it disables the endpoint.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var response = await PostEventAsync("checkout.session.completed", $$"""
            {
              "id": "cs_nometa",
              "object": "checkout.session",
              "customer": "{{KnownCustomerId}}"
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync())!.SubscriptionStatus.Should().Be("active");
    }

    #endregion

    #region Admission predicates

    // ShouldAdopt and IsForCurrentSubscription decide whether an event may write user state. Both
    // sit behind a Stripe API call in their handlers, so they are exercised directly rather than
    // through the endpoint, which would need live network.

    [Fact]
    public void ShouldAdopt_GivenReplacementSubscriptionAfterCancellation_AdoptsIt()
    {
        // The stored id is not a high-water mark: cancellation leaves it pointing at the dead
        // subscription. Reading "different id" as "older" would misfile a resubscribe as stale and
        // strand the account on the cancelled subscription.
        var user = NewUser(subscriptionId: "sub_old", status: "canceled");

        StripeWebhook.ShouldAdopt(user, new Subscription { Id = "sub_new", Status = "active" },
            NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void ShouldAdopt_GivenStaleEchoOfAReplacedSubscription_DeclinesIt()
    {
        var user = NewUser(subscriptionId: CurrentSubscriptionId, status: "active");

        StripeWebhook.ShouldAdopt(user, new Subscription { Id = "sub_old", Status = "canceled" },
            NullLogger.Instance).Should().BeFalse();
    }

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    public void ShouldAdopt_GivenAnyLiveStatus_AdoptsIt(string status)
    {
        var user = NewUser(subscriptionId: "sub_old", status: "canceled");

        StripeWebhook.ShouldAdopt(user, new Subscription { Id = "sub_new", Status = status },
            NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void ShouldAdopt_GivenUserWithNoSubscriptionYet_AdoptsIt()
    {
        StripeWebhook.ShouldAdopt(NewUser(), new Subscription { Id = "sub_new", Status = "canceled" },
            NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void IsForCurrentSubscription_GivenInvoiceForAnotherSubscription_IsFalse()
    {
        var user = NewUser(subscriptionId: CurrentSubscriptionId);

        StripeWebhook.IsForCurrentSubscription(
            user, InvoiceFor("sub_old"), NullLogger.Instance, "x").Should().BeFalse();
    }

    [Fact]
    public void IsForCurrentSubscription_GivenInvoiceWithNoSubscription_IsFalse()
    {
        var user = NewUser(subscriptionId: CurrentSubscriptionId);

        StripeWebhook.IsForCurrentSubscription(
            user, new Invoice { Id = "in_x" }, NullLogger.Instance, "x").Should().BeFalse();
    }

    [Fact]
    public void IsForCurrentSubscription_GivenMatchingSubscription_IsTrue()
    {
        var user = NewUser(subscriptionId: CurrentSubscriptionId);

        StripeWebhook.IsForCurrentSubscription(
            user, InvoiceFor(CurrentSubscriptionId), NullLogger.Instance, "x").Should().BeTrue();
    }

    #endregion

    #region Event ledger

    [Fact]
    public async Task HandleWebhook_RecordsTheEventTypeItProcessed()
    {
        // The provider's event id is opaque, so without the type the ledger cannot answer
        // which event types are actually reaching their handlers.
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        await PostEventAsync("invoice.payment_failed", InvoiceJson());

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        db.ProcessedWebhookEvents.Single().EventType.Should().Be("invoice.payment_failed");
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
        await SeedUserAsync(status: "active", subscriptionId: CurrentSubscriptionId);

        var first = await PostEventAsync(
            "invoice.payment_failed", InvoiceJson(), eventId: "evt_dup");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await PostEventAsync(
            "invoice.payment_failed", InvoiceJson(), eventId: "evt_dup");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("duplicate");
    }

    #endregion

    #region Helpers

    private static User NewUser(string? subscriptionId = null, string? status = null) => new()
    {
        StytchUserId = "stytch_user",
        Email = "user@example.com",
        StripeCustomerId = KnownCustomerId,
        StripeSubscriptionId = subscriptionId,
        SubscriptionStatus = status,
    };

    private static string InvoiceJson(
        string customerId = KnownCustomerId,
        string? subscriptionId = CurrentSubscriptionId,
        string metadata = "{}")
    {
        var parent = subscriptionId is null
            ? "null"
            : $$"""{ "type": "subscription_details", "subscription_details": { "subscription": "{{subscriptionId}}" } }""";

        return $$"""
            {
              "id": "in_test",
              "object": "invoice",
              "customer": "{{customerId}}",
              "metadata": {{metadata}},
              "parent": {{parent}}
            }
            """;
    }

    private static string SubscriptionJson(
        string subscriptionId = CurrentSubscriptionId,
        string? items = null)
    {
        items ??= $$"""[ { "id": "si_1", "object": "subscription_item", "current_period_end": {{PeriodEndUnix}} } ]""";

        return $$"""
            {
              "id": "{{subscriptionId}}",
              "object": "subscription",
              "customer": "{{KnownCustomerId}}",
              "metadata": {},
              "items": { "object": "list", "data": {{items}} }
            }
            """;
    }

    private static Invoice InvoiceFor(string subscriptionId) => new()
    {
        Id = "in_x",
        Parent = new InvoiceParent
        {
            SubscriptionDetails = new InvoiceParentSubscriptionDetails
            {
                SubscriptionId = subscriptionId,
            },
        },
    };

    private Task<HttpResponseMessage> PostEventAsync(
        string type, string dataObject, string? eventId = null)
    {
        // api_version must match the SDK's, because ConstructEvent is called with
        // throwOnApiVersionMismatch: true. Reading it at runtime keeps these tests working across
        // SDK upgrades instead of pinning a version string that goes stale.
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
        string status, string? subscriptionId = null, DateTimeOffset? expiresAt = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        var user = NewUser(subscriptionId, status);
        user.SubscriptionExpiresAt = expiresAt;
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    private async Task<User?> GetUserAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        return await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == KnownCustomerId);
    }

    private async Task<int> CountRecordedAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchNotesDbContext>();
        return await db.ProcessedWebhookEvents.CountAsync();
    }

    #endregion
}
