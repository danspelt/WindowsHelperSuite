namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>Learned typo → correction (never auto-applied; used for suggestions).</summary>
public sealed class CorrectionEntry
{
    public string Typo { get; set; } = string.Empty;
    public string Fix { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

/// <summary>Maps a typed prefix (e.g. first word) to full phrase completions.</summary>
public sealed class PhrasePrefixBucket
{
    public string Prefix { get; set; } = string.Empty;
    public List<string> Phrases { get; set; } = [];
}

/// <summary>Optional standalone file merged at load: %AppData%/WindowsHelperSuite/data/corrections.json</summary>
public sealed class CorrectionsFile
{
    public List<CorrectionEntry> Corrections { get; set; } = [];
}
