namespace WindowsHelperSuite.AI.Models;

/// <summary>
/// Request for AI text rewriting.
/// </summary>
public class AiRewriteRequest
{
    public string Text { get; set; } = string.Empty;
    public RewriteTone Tone { get; set; } = RewriteTone.Clearer;
}

public enum RewriteTone
{
    Clearer,
    Shorter,
    MoreFormal,
    Friendlier,
    FixGrammar
}
