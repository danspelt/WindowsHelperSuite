namespace WindowsHelperSuite.Writer.Models;

public sealed class PredictionResult
{
    public IReadOnlyList<PredictionCandidate> Suggestions { get; init; } = [];
    public bool UsedFallback { get; init; }
    public string? DebugInfo { get; init; }
}
