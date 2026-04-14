namespace WindowsHelperSuite.Writer.Models;

/// <summary>Helpers for sentence buffers where <see cref="PredictionRequest.CurrentToken"/> is not yet committed.</summary>
public static class WriterSentenceContext
{
    /// <summary>
    /// Last whitespace-delimited token in <paramref name="contextBeforeCurrentToken"/> — the completed word
    /// immediately before what the user is typing (InputService suggestion context prefix).
    /// </summary>
    public static string LastCompletedWord(string contextBeforeCurrentToken)
    {
        if (string.IsNullOrWhiteSpace(contextBeforeCurrentToken))
        {
            return string.Empty;
        }

        var parts = contextBeforeCurrentToken.Trim().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[^1] : string.Empty;
    }
}
