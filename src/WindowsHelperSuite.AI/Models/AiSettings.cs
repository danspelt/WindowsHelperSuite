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
}
