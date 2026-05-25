namespace WindowsHelperSuite.Writer.Config;

public sealed class WriterPredictionOptions
{
    public double PrefixWordTrust { get; set; } = 1.0;
    public double PhraseMemoryTrust { get; set; } = 1.15;
    public double RecencyTrust { get; set; } = 1.05;
    public double LocalLlmTrust { get; set; } = 0.95;
    public double CorrectionTrust { get; set; } = 1.2;
    public double NextWordTrust { get; set; } = 1.25;
}
