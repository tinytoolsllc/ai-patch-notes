namespace PatchNotes.Data;

public class SentDigestEmail
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string UserId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string RecipientEmail { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ResendEmailId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset SentAt { get; set; }

    public User User { get; set; } = null!;
}
