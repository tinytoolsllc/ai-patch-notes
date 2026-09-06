using Microsoft.EntityFrameworkCore;
using PatchNotes.Data;
using Stripe;

namespace PatchNotes.Api.Webhooks;

public static class StripeWebhook
{
    public static WebApplication MapStripeWebhook(this WebApplication app)
    {
        // POST /webhooks/stripe - Handle Stripe webhook events
        app.MapPost("/webhooks/stripe", async (
            HttpContext httpContext,
            PatchNotesDbContext db,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PatchNotes.Api.Webhooks.StripeWebhook");

            // CRITICAL: Fail early if webhook secret is not configured
            var webhookSecret = configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                logger.LogError("Stripe:WebhookSecret is not configured. Rejecting webhook to prevent unverified payloads");
                return Results.StatusCode(503);
            }

            // Read the raw body for signature verification
            using var reader = new StreamReader(httpContext.Request.Body);
            var body = await reader.ReadToEndAsync();

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    body,
                    httpContext.Request.Headers["Stripe-Signature"],
                    webhookSecret,
                    throwOnApiVersionMismatch: true
                );
            }
            catch (StripeException ex)
            {
                logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
                return Results.BadRequest(new { error = "Invalid signature" });
            }

            // Idempotency: skip already-processed events
            if (await db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == stripeEvent.Id))
            {
                logger.LogInformation("Skipping already-processed Stripe event {EventId}", stripeEvent.Id);
                return Results.Ok(new { received = true, duplicate = true });
            }

            // Filter events to only those for our app. Two checks, because neither alone
            // covers every event we handle:
            //
            //   - Metadata. Stripe never copies a Checkout Session's own metadata onto the
            //     subscription it creates, so only sessions carried the "app" tag until
            //     SubscriptionData.Metadata was added at checkout. Subscriptions created since
            //     then carry it too, which makes them recognisable no matter what order Stripe
            //     delivers the events in.
            //   - Known customer. Invoices never carry the tag, and subscriptions created before
            //     that change never will. Those are matched by the customer we already stored.
            //
            // Anything we cannot place is acknowledged and dropped.
            if (!HasAppMetadata(stripeEvent.Data.Object)
                && !await IsKnownCustomerAsync(stripeEvent.Data.Object, db))
            {
                logger.LogInformation(
                    "Ignoring Stripe event {EventId} ({EventType}): not associated with this app",
                    stripeEvent.Id, stripeEvent.Type);
                return Results.Ok(new { received = true, ignored = true });
            }

            try
            {
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        await HandleCheckoutSessionCompleted(stripeEvent, db, logger);
                        break;

                    case "customer.subscription.updated":
                        await HandleSubscriptionUpdated(stripeEvent, db, logger);
                        break;

                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(stripeEvent, db, logger);
                        break;

                    case "invoice.payment_failed":
                        await HandlePaymentFailed(stripeEvent, db, logger);
                        break;

                    case "invoice.payment_succeeded":
                        await HandlePaymentSucceeded(stripeEvent, db, logger);
                        break;

                    default:
                        logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                        break;
                }

                // Record event as processed for idempotency
                db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
                {
                    EventId = stripeEvent.Id,
                    ProcessedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();

                return Results.Ok(new { received = true });
            }
            catch (StripeException ex)
            {
                logger.LogError(ex, "Stripe API error while handling webhook event {EventId}: {Message}", stripeEvent.Id, ex.Message);
                return Results.StatusCode(503);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling Stripe webhook event {EventId}: {Message}", stripeEvent.Id, ex.Message);
                return Results.Problem("Error processing webhook");
            }
        })
        .ExcludeFromDescription();

        return app;
    }

    private const string AppMetadataKey = "app";
    private const string AppMetadataValue = "patchnotes";

    /// <summary>Metadata we set ourselves, on sessions and on subscriptions created at checkout.</summary>
    private static bool HasAppMetadata(object? data) =>
        data is IHasMetadata { Metadata: { } metadata }
        && metadata.TryGetValue(AppMetadataKey, out var app)
        && app == AppMetadataValue;

    /// <summary>
    /// The customer this event concerns, for the event types we handle. Stripe.net has no common
    /// interface for it, so the types are listed explicitly; anything else is not ours to judge.
    /// </summary>
    private static string? GetCustomerId(object? data) => data switch
    {
        Stripe.Checkout.Session session => session.CustomerId,
        Subscription subscription => subscription.CustomerId,
        Invoice invoice => invoice.CustomerId,
        _ => null,
    };

    private static async Task<bool> IsKnownCustomerAsync(object? data, PatchNotesDbContext db)
    {
        var customerId = GetCustomerId(data);
        return !string.IsNullOrEmpty(customerId)
            && await db.Users.AnyAsync(u => u.StripeCustomerId == customerId);
    }

    /// <summary>
    /// Re-reads the subscription from Stripe. Snapshot payloads are eventually consistent and can
    /// arrive out of order, so the payload identifies the subscription but never decides its state.
    /// </summary>
    private static Task<Subscription> FetchCurrentAsync(Subscription payload) =>
        new SubscriptionService().GetAsync(payload.Id);

    /// <summary>
    /// True when the event concerns a subscription the user has already replaced. Re-fetching does
    /// not catch this on its own: a cancelled subscription still reads back as cancelled, so a
    /// delayed "deleted" for a previous subscription would cancel the current one.
    /// </summary>
    private static bool IsSupersededSubscription(User user, Subscription payload, ILogger logger)
    {
        if (string.IsNullOrEmpty(user.StripeSubscriptionId)
            || user.StripeSubscriptionId == payload.Id)
        {
            return false;
        }

        logger.LogInformation(
            "Ignoring event for superseded subscription {EventSubscriptionId}; user is on {CurrentSubscriptionId}",
            payload.Id, user.StripeSubscriptionId);
        return true;
    }

    private static async Task HandleCheckoutSessionCompleted(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        // Get the Stytch user ID from session metadata. Metadata is not guaranteed here: a
        // session now reaches this handler when it merely belongs to a customer we know, so the
        // null check the ownership filter used to provide has to live at the point of use.
        if (session.Metadata is not { } metadata
            || !metadata.TryGetValue("stytch_user_id", out var stytchUserId))
        {
            logger.LogWarning(
                "Checkout session {SessionId} completed with no stytch_user_id in metadata",
                session.Id);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stytch ID: {StytchUserId}", stytchUserId);
            return;
        }

        // Update user with Stripe customer ID
        user.StripeCustomerId = session.CustomerId;

        // Fetch the subscription to get status and period end
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(session.SubscriptionId);

            user.StripeSubscriptionId = subscription.Id;
            user.SubscriptionStatus = subscription.Status;
            user.SubscriptionExpiresAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Updated subscription for user {StytchUserId}: status={Status}", stytchUserId, user.SubscriptionStatus);
    }

    private static async Task HandleSubscriptionUpdated(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Subscription payload) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == payload.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", payload.CustomerId);
            return;
        }

        if (IsSupersededSubscription(user, payload, logger)) return;

        var subscription = await FetchCurrentAsync(payload);
        user.StripeSubscriptionId = subscription.Id;
        user.SubscriptionStatus = subscription.Status;
        user.SubscriptionExpiresAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;

        await db.SaveChangesAsync();
        logger.LogInformation("Updated subscription for customer {CustomerId}: status={Status}", subscription.CustomerId, subscription.Status);
    }

    private static async Task HandleSubscriptionDeleted(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Subscription payload) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == payload.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", payload.CustomerId);
            return;
        }

        if (IsSupersededSubscription(user, payload, logger)) return;

        var subscription = await FetchCurrentAsync(payload);
        user.SubscriptionStatus = "canceled";
        // Keep the expiration date so user has access until end of paid period
        user.SubscriptionExpiresAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;

        await db.SaveChangesAsync();
        logger.LogInformation("Subscription canceled for customer {CustomerId}", subscription.CustomerId);
    }

    private static async Task HandlePaymentFailed(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", invoice.CustomerId);
            return;
        }

        user.SubscriptionStatus = "past_due";

        await db.SaveChangesAsync();
        logger.LogWarning("Payment failed for customer {CustomerId}, marked as past_due", invoice.CustomerId);
    }

    private static async Task HandlePaymentSucceeded(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", invoice.CustomerId);
            return;
        }

        // Update subscription expiry on successful renewal payment
        var invoiceSubscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (!string.IsNullOrEmpty(invoiceSubscriptionId))
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(invoiceSubscriptionId);

            user.SubscriptionStatus = subscription.Status;
            user.SubscriptionExpiresAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;

            await db.SaveChangesAsync();
            logger.LogInformation("Payment succeeded for customer {CustomerId}, updated expiry to {ExpiresAt}", invoice.CustomerId, user.SubscriptionExpiresAt);
        }
    }
}
