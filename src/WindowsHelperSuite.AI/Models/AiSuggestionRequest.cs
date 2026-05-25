namespace WindowsHelperSuite.AI.Models;

/// <summary>
/// Request for AI phrase suggestions.
/// </summary>
public class AiSuggestionRequest
{
    public string CurrentText { get; set; } = string.Empty;
    public string CurrentWord { get; set; } = string.Empty;

    /// <summary>Last completed word before <see cref="CurrentWord"/> (empty after Space).</summary>
    public string? PreviousCompletedWord { get; set; }

    public string? PreviousSentence { get; set; }
    public int MaxSuggestions { get; set; } = 2;
}
