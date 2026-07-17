namespace PatchNotes.Sync.Core.AI;

/// <summary>
/// Options for configuring the AI client.
/// </summary>
public class AiClientOptions
{
    /// <summary>
    /// The section name in configuration.
    /// </summary>
    public const string SectionName = "AI";

    /// <summary>
    /// The AI provider API base URL.
    /// Supports OpenAI-compatible APIs (OpenAI, Groq, OpenRouter, etc.).
    /// </summary>
    public string BaseUrl { get; set; } = "https://ollama.com/v1/";

    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model to use for summarization.
    /// </summary>
    public string Model { get; set; } = "gemma4:31b";
}
