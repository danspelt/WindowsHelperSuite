namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>Personal word frequency for ranking (writer-model.json).</summary>
public sealed class TypingWordEntry
{
    public string Word { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> ContextCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Learned phrase with first-word prefix bucket.</summary>
public sealed class TypingPhraseEntry
{
    public string Phrase { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastUsed { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public Dictionary<string, int> ContextCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Learned typo → correction (personal layer).</summary>
public sealed class TypingCorrectionRecord
{
    public string Typed { get; set; } = string.Empty;
    public string Corrected { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}
