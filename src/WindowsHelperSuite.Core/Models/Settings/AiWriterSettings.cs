namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>AI features for Writer learning (OpenAI-compatible Chat Completions API).</summary>
public class AiWriterSettings
{
    /// <summary>When true and <see cref="ApiKey"/> is set, new words not already in the word bank are sent to the model; only positive answers are saved.</summary>
    public bool EnableVocabularyGate { get; set; } = true;

    /// <summary>Bearer token for <c>Authorization: Bearer</c> (e.g. OpenAI API key).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Chat model id (e.g. gpt-4o-mini).</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>API root without trailing slash, e.g. https://api.openai.com/v1</summary>
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Timeout for the vocabulary gate HTTP call.</summary>
    public int VocabularyGateTimeoutMs { get; set; } = 3500;

    /// <summary>If the gate is enabled but the request fails or times out, still add the word to the word bank when true.</summary>
    public bool FallbackSaveOnAiFailure { get; set; } = true;

    // ── Chat window settings (persisted) ──

    /// <summary>Use streaming responses in the chat window.</summary>
    public bool ChatUseStreaming { get; set; } = true;

    /// <summary>Temperature for chat completions (0.0–2.0).</summary>
    public double ChatTemperature { get; set; } = 0.7;

    /// <summary>HTTP timeout in seconds for chat requests.</summary>
    public int ChatTimeoutSeconds { get; set; } = 120;

    /// <summary>Default system prompt injected at the start of every conversation.</summary>
    public string ChatSystemPrompt { get; set; } =
        "You are a helpful, clear, and concise assistant. " +
        "Keep replies short unless the user asks for detail. " +
        "Produce accessible, easy-to-read writing. " +
        "Be supportive and friendly.";
}
