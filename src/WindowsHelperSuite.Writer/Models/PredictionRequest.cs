namespace WindowsHelperSuite.Writer.Models;

public sealed class PredictionRequest
{
    public string FullText { get; init; } = "";
    public string CurrentSentence { get; init; } = "";
    /// <summary>Completed word right before <see cref="CurrentToken"/> (from context prefix only; not the last char of <see cref="CurrentSentence"/> when it includes the partial).</summary>
    public string PreviousCompletedWord { get; init; } = "";
    public string CurrentToken { get; init; } = "";
    public int CaretIndex { get; init; }
    public WriterContextSnapshot Context { get; init; } = new();
    public int MaxSuggestions { get; init; } = 5;

    /// <summary>When true, slow providers (for example local LLM) are skipped so the UI path stays under ~50ms.</summary>
    public bool PreferLocalOnly { get; init; } = true;
}
