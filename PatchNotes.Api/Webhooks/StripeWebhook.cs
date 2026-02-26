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

            // Filter events to only those for our app
            if (stripeEvent.Data.Object is IHasMetadata objWithMetadata)
            {
                var metadata = objWithMetadata.Metadata;
                if (metadata == null || !metadata.TryGetValue("app", out var appValue) || appValue != "patchnotes")
                {
                    // Not our event, ignore but acknowledge
                    return Results.Ok(new { received = true, ignored = true });
                }
            }
            else
            {
                // Unknown object type without metadata — skip to be safe
                logger.LogWarning("Stripe event {EventType} data object does not support metadata, skipping", stripeEvent.Type);
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
        });

        return app;
    }

    private static async Task HandleCheckoutSessionCompleted(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        // Get the Stytch user ID from session metadata
        if (!session.Metadata.TryGetValue("stytch_user_id", out var stytchUserId))
        {
            logger.LogWarning("Checkout session completed but no stytch_user_id in metadata");
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
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", subscription.CustomerId);
            return;
        }

        user.StripeSubscriptionId = subscription.Id;
        user.SubscriptionStatus = subscription.Status;
        user.SubscriptionExpiresAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd;

        await db.SaveChangesAsync();
        logger.LogInformation("Updated subscription for customer {CustomerId}: status={Status}", subscription.CustomerId, subscription.Status);
    }

    private static async Task HandleSubscriptionDeleted(Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", subscription.CustomerId);
            return;
        }

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
