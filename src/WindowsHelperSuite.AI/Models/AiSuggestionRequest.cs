namespace WindowsHelperSuite.AI.Models;

/// <summary>
/// Request for AI phrase suggestions.
/// </summary>
public class AiSuggestionRequest
{
    public string CurrentText { get; set; } = string.Empty;
    public string CurrentWord { get; set; } = string.Empty;
    public string? PreviousSentence { get; set; }
    public int MaxSuggestions { get; set; } = 2;
}
