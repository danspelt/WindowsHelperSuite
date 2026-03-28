namespace WindowsHelperSuite.AI.Models;

/// <summary>
/// A single AI-generated suggestion result.
/// </summary>
public class AiSuggestionResult
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool IsPhraseCompletion { get; set; }
}
