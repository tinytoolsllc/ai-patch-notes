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

            // The only global check is a negative one: reject what is provably another app's.
            //
            // There is deliberately no positive "this looks like ours" gate. Metadata alone misses
            // invoices and any subscription predating subscription_data.metadata, and widening it
            // with "the customer exists in our Users table" trades a strong signal for a weak one
            // -- a stored customer is not proof the event concerns us -- while giving handlers a
            // false assurance that admission had already been decided for them. Each handler below
            // resolves its own subject and returns false without writing when it cannot.
            if (IsTaggedForAnotherApp(stripeEvent.Data.Object))
            {
                logger.LogInformation(
                    "Ignoring Stripe event {EventId} ({EventType}): tagged for another app",
                    stripeEvent.Id, stripeEvent.Type);
                return Results.Ok(new { received = true, ignored = true });
            }

            try
            {
                bool applied;
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        applied = await HandleCheckoutSessionCompleted(stripeEvent, db, logger);
                        break;

                    case "customer.subscription.updated":
                        applied = await HandleSubscriptionUpdated(stripeEvent, db, logger);
                        break;

                    case "customer.subscription.deleted":
                        applied = await HandleSubscriptionDeleted(stripeEvent, db, logger);
                        break;

                    case "invoice.payment_failed":
                        applied = await HandlePaymentFailed(stripeEvent, db, logger);
                        break;

                    case "invoice.payment_succeeded":
                        applied = await HandlePaymentSucceeded(stripeEvent, db, logger);
                        break;

                    default:
                        logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                        applied = false;
                        break;
                }

                // Only events that actually changed something are recorded. This table means "do
                // not process this again", and claiming that for an event no handler applied burns
                // it permanently: a subscription event that arrives before we can resolve its user
                // would be consumed on first delivery and could never be replayed.
                if (applied)
                {
                    db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
                    {
                        EventId = stripeEvent.Id,
                        ProcessedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync();
                }

                return Results.Ok(new { received = true, applied });
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
    private const string StytchUserIdMetadataKey = "stytch_user_id";

    /// <summary>
    /// Subscription statuses that mean the customer is actually on this subscription. Anything
    /// outside this set is history, which is what distinguishes a replacement subscription from a
    /// stale echo of a previous one.
    /// </summary>
    private static readonly HashSet<string> LiveSubscriptionStatuses =
        new(StringComparer.Ordinal) { "active", "trialing", "past_due", "unpaid", "incomplete" };

    /// <summary>
    /// True only when the event carries an explicit app tag naming someone else. An absent tag is
    /// not evidence either way -- invoices never carry one -- so it is not treated as rejection.
    /// </summary>
    private static bool IsTaggedForAnotherApp(object? data) =>
        data is IHasMetadata { Metadata: { } metadata }
        && metadata.TryGetValue(AppMetadataKey, out var app)
        && app != AppMetadataValue;

    /// <summary>
    /// Re-reads the subscription from Stripe. Snapshot payloads are eventually consistent, so the
    /// payload identifies the subscription but never decides its state.
    /// </summary>
    private static Task<Subscription> FetchCurrentAsync(Subscription payload) =>
        new SubscriptionService().GetAsync(payload.Id);

    /// <summary>
    /// The user a subscription event concerns. The customer id is the usual route, but Stripe can
    /// deliver subscription events before <c>checkout.session.completed</c> has stored one, so the
    /// stytch_user_id written to subscription_data.metadata at checkout is the fallback.
    /// </summary>
    private static async Task<User?> ResolveUserAsync(Subscription payload, PatchNotesDbContext db)
    {
        if (!string.IsNullOrEmpty(payload.CustomerId))
        {
            var byCustomer = await db.Users
                .FirstOrDefaultAsync(u => u.StripeCustomerId == payload.CustomerId);
            if (byCustomer != null)
            {
                return byCustomer;
            }
        }

        if (payload.Metadata is { } metadata
            && metadata.TryGetValue(StytchUserIdMetadataKey, out var stytchUserId)
            && !string.IsNullOrEmpty(stytchUserId))
        {
            return await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
        }

        return null;
    }

    /// <summary>
    /// Whether an event about this subscription may write the user's subscription state.
    ///
    /// The stored id is not a high-water mark: only checkout advances it and cancellation leaves it
    /// pointing at the dead subscription, so "a different id" cannot be read as "an older one". A
    /// resubscribe would be misfiled as stale on that reading. The subscription's own status is the
    /// signal that works -- a live subscription is the one the customer is on.
    /// </summary>
    internal static bool ShouldAdopt(User user, Subscription subscription, ILogger logger)
    {
        if (string.IsNullOrEmpty(user.StripeSubscriptionId)
            || user.StripeSubscriptionId == subscription.Id
            || LiveSubscriptionStatuses.Contains(subscription.Status))
        {
            return true;
        }

        logger.LogInformation(
            "Ignoring {Status} subscription {EventSubscriptionId}; user is on {CurrentSubscriptionId}",
            subscription.Status, subscription.Id, user.StripeSubscriptionId);
        return false;
    }

    /// <summary>
    /// Whether an invoice belongs to the subscription the user is currently on. Stripe retries a
    /// failed invoice for weeks, so a replaced subscription keeps producing invoice events long
    /// after the customer resubscribed; acting on those reports dunning for an account in good
    /// standing, or rewinds the paid period to the old subscription's. An invoice with no
    /// subscription is not ours to act on either.
    /// </summary>
    internal static bool IsForCurrentSubscription(
        User user, Invoice invoice, ILogger logger, string action)
    {
        var invoiceSubscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;

        if (!string.IsNullOrEmpty(invoiceSubscriptionId)
            && invoiceSubscriptionId == user.StripeSubscriptionId)
        {
            return true;
        }

        logger.LogInformation(
            "Ignoring {Action} for invoice {InvoiceId} on subscription {InvoiceSubscriptionId}; user is on {CurrentSubscriptionId}",
            action, invoice.Id, invoiceSubscriptionId ?? "(none)", user.StripeSubscriptionId ?? "(none)");
        return false;
    }

    /// <summary>
    /// Copies subscription state onto the user. The expiry is overwritten only when the
    /// subscription carries one: Items can come back empty, and <see cref="User.IsPro"/> reads a
    /// null expiry as "no paid period remaining", so writing null ends access immediately.
    /// </summary>
    private static void ApplySubscription(User user, Subscription subscription)
    {
        user.StripeSubscriptionId = subscription.Id;
        user.SubscriptionStatus = subscription.Status;
        user.SubscriptionExpiresAt =
            subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd ?? user.SubscriptionExpiresAt;
    }

    private static async Task<bool> HandleCheckoutSessionCompleted(
        Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session) return false;

        // Metadata is not guaranteed: nothing upstream vets it, so the check lives at the point
        // of use rather than being assumed from an admission filter.
        if (session.Metadata is not { } metadata
            || !metadata.TryGetValue(StytchUserIdMetadataKey, out var stytchUserId))
        {
            logger.LogWarning(
                "Checkout session {SessionId} completed with no stytch_user_id in metadata",
                session.Id);
            return false;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.StytchUserId == stytchUserId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stytch ID: {StytchUserId}", stytchUserId);
            return false;
        }

        // Update user with Stripe customer ID
        user.StripeCustomerId = session.CustomerId;

        // Fetch the subscription to get status and period end
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            var subscription = await new SubscriptionService().GetAsync(session.SubscriptionId);
            ApplySubscription(user, subscription);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Updated subscription for user {StytchUserId}: status={Status}", stytchUserId, user.SubscriptionStatus);
        return true;
    }

    private static async Task<bool> HandleSubscriptionUpdated(
        Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Subscription payload) return false;

        var user = await ResolveUserAsync(payload, db);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", payload.CustomerId);
            return false;
        }

        var subscription = await FetchCurrentAsync(payload);
        if (!ShouldAdopt(user, subscription, logger)) return false;

        // Reached via subscription metadata when checkout has not landed yet, so the customer id
        // may still be missing.
        if (string.IsNullOrEmpty(user.StripeCustomerId))
        {
            user.StripeCustomerId = subscription.CustomerId;
        }

        ApplySubscription(user, subscription);

        await db.SaveChangesAsync();
        logger.LogInformation("Updated subscription for customer {CustomerId}: status={Status}", subscription.CustomerId, subscription.Status);
        return true;
    }

    private static async Task<bool> HandleSubscriptionDeleted(
        Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Subscription payload) return false;

        var user = await ResolveUserAsync(payload, db);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", payload.CustomerId);
            return false;
        }

        // A cancellation only concerns the subscription the user is actually on.
        if (!string.IsNullOrEmpty(user.StripeSubscriptionId)
            && user.StripeSubscriptionId != payload.Id)
        {
            logger.LogInformation(
                "Ignoring cancellation of subscription {EventSubscriptionId}; user is on {CurrentSubscriptionId}",
                payload.Id, user.StripeSubscriptionId);
            return false;
        }

        // No re-fetch here. The status is already known, and the period end is in the payload that
        // was signature-verified, so re-reading would only add a dependency on Stripe being
        // reachable for a cancellation to be recorded at all.
        user.SubscriptionStatus = "canceled";
        // Keep the expiration date so user has access until end of paid period
        user.SubscriptionExpiresAt =
            payload.Items.Data.FirstOrDefault()?.CurrentPeriodEnd ?? user.SubscriptionExpiresAt;

        await db.SaveChangesAsync();
        logger.LogInformation("Subscription canceled for customer {CustomerId}", payload.CustomerId);
        return true;
    }

    private static async Task<bool> HandlePaymentFailed(
        Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Invoice invoice) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", invoice.CustomerId);
            return false;
        }

        if (!IsForCurrentSubscription(user, invoice, logger, "payment failure")) return false;

        user.SubscriptionStatus = "past_due";

        await db.SaveChangesAsync();
        logger.LogWarning("Payment failed for customer {CustomerId}, marked as past_due", invoice.CustomerId);
        return true;
    }

    private static async Task<bool> HandlePaymentSucceeded(
        Event stripeEvent, PatchNotesDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Invoice invoice) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (user == null)
        {
            logger.LogWarning("User not found for Stripe customer: {CustomerId}", invoice.CustomerId);
            return false;
        }

        if (!IsForCurrentSubscription(user, invoice, logger, "payment success")) return false;

        // Update subscription expiry on successful renewal payment
        var invoiceSubscriptionId = invoice.Parent!.SubscriptionDetails!.SubscriptionId;
        var subscription = await new SubscriptionService().GetAsync(invoiceSubscriptionId);
        ApplySubscription(user, subscription);

        await db.SaveChangesAsync();
        logger.LogInformation("Payment succeeded for customer {CustomerId}, updated expiry to {ExpiresAt}", invoice.CustomerId, user.SubscriptionExpiresAt);
        return true;
    }
}
