namespace PatchNotes.Data;

public class ProcessedWebhookEvent
{
    public required string EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>
    /// What kind of event this was, in the provider's own vocabulary -- "invoice.payment_succeeded"
    /// from Stripe, "user.CREATE" from Stytch. The provider's event id is opaque, so without this
    /// the ledger cannot answer which event types are actually reaching their handlers.
    /// Null for rows written before this column existed; it cannot be backfilled from the id alone.
    /// </summary>
    public string? EventType { get; set; }
}
