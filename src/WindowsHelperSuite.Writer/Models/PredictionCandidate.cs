namespace WindowsHelperSuite.Writer.Models;

public sealed class PredictionCandidate
{
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public double BaseScore { get; init; }
    public double FinalScore { get; set; }
    public bool IsPhrase { get; init; }
    public bool RequiresTrailingSpace { get; init; } = true;
}
