namespace PatchNotes.Data;

public class User : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = IdGenerator.NewId();

    /// <summary>
    /// The Stytch user ID (e.g., "user-live-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")
    /// </summary>
    public required string StytchUserId { get; set; }

    /// <summary>
    /// Primary email address from Stytch
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// User's name if provided via OAuth or profile
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// When the user first authenticated (created in our system)
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the user record was last updated from Stytch
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Last time the user logged in
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Stripe customer ID for subscription management
    /// </summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>
    /// Stripe subscription ID for the Pro plan
    /// </summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>
    /// Current subscription status: "active", "canceled", "past_due", or null
    /// </summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>
    /// When the current subscription period expires
    /// </summary>
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }

    /// <summary>
    /// Whether the user wants to receive weekly digest emails
    /// </summary>
    public bool EmailDigestEnabled { get; set; } = false;

    /// <summary>
    /// Day of week to send digest email: 0=Sunday through 6=Saturday. Default 1 = Monday.
    /// </summary>
    public int DigestDay { get; set; } = 1;

    /// <summary>
    /// Hour of day (UTC) to send digest email: 0-23. Default 9 = 4am EST.
    /// </summary>
    public int DigestHour { get; set; } = 9;

    /// <summary>
    /// Whether the welcome email has been sent to this user
    /// </summary>
    public bool EmailWelcomeSent { get; set; }

    /// <summary>
    /// Whether the user has an active Pro subscription
    /// </summary>
    public bool IsPro =>
        SubscriptionStatus == "active" ||
        SubscriptionStatus == "trialing" ||
        (SubscriptionStatus == "past_due" && SubscriptionExpiresAt > DateTimeOffset.UtcNow) ||
        (SubscriptionStatus == "canceled" && SubscriptionExpiresAt > DateTimeOffset.UtcNow);

    public ICollection<Watchlist> Watchlists { get; set; } = [];
}
