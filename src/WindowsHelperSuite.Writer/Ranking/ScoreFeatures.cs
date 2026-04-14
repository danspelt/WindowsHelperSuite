namespace WindowsHelperSuite.Writer.Ranking;

/// <summary>Optional feature vector for future learned ranking; populated incrementally by <see cref="CandidateRanker"/>.</summary>
public readonly struct ScoreFeatures
{
    public double PrefixStrength { get; init; }
    public double FrequencyNorm { get; init; }
    public double RecencyNorm { get; init; }
    public double SourceTrust { get; init; }
}
