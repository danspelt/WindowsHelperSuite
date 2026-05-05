namespace WindowsHelperSuite.AI.Models;

/// <summary>
/// Settings for AI features in the Writer module.
/// </summary>
public class AiSettings
{
    public bool EnableAiSuggestions { get; set; } = true;
    public bool EnableAiPhraseCompletion { get; set; } = true;
    public bool EnableAiRewriteTools { get; set; } = true;
    public bool UseLocalSuggestionsFirst { get; set; } = true;
    public int MaxAiSuggestions { get; set; } = 2;
    public int AiTimeoutMs { get; set; } = 400;
    public bool RememberFrequentPhrases { get; set; } = true;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>OpenAI-compatible API root, no trailing slash (e.g. https://api.openai.com/v1).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    // ── Enhanced Writer AI features ──

    /// <summary>Enable AI sentence completion suggestions.</summary>
    public bool EnableSentenceCompletion { get; set; } = true;

    /// <summary>Enable AI grammar and spelling correction.</summary>
    public bool EnableGrammarCorrection { get; set; } = true;

    /// <summary>Auto-apply instant fixes for common typos.</summary>
    public bool EnableAutoInstantFixes { get; set; } = true;

    /// <summary>When true, AI learns from user's writing patterns.</summary>
    public bool EnableContextLearning { get; set; } = true;

    /// <summary>Enable style/tone variation suggestions.</summary>
    public bool EnableStyleVariations { get; set; } = false;

    /// <summary>Timeout for grammar correction calls (faster than general AI).</summary>
    public int GrammarTimeoutMs { get; set; } = 300;
}
