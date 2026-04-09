namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>
/// Characters that extend the in-memory “current word” in <c>InputService</c> and the string passed to
/// <c>IPredictionService.GetSuggestions</c> as <c>currentWord</c>. Keep in sync with paste handling and backspace.
/// </summary>
public static class WriterWordBufferPolicy
{
    public static bool IsWordExtendingCharacter(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-';

    /// <summary>
    /// Whether this keystroke may start a Writer typing session or restore validated text input after caret loss.
    /// Digits, symbols, and punctuation do not wake the session; see <c>InputService.OnKeyPressed</c>.
    /// </summary>
    public static bool CanStartWriterSessionFromKeystroke(char ch) => char.IsLetter(ch);

    /// <summary>
    /// For text before the current partial word, finds where deletion should start to remove the last whitespace-delimited
    /// token and any spaces between that token and the partial. Used by <c>InputService</c> for Left-arrow delete-word.
    /// </summary>
    /// <param name="textBeforePartial">Prefix of the sentence buffer before the partial (see <c>GetTextBeforeCurrentWordLocked</c>).</param>
    /// <param name="startIndex">Index in <paramref name="textBeforePartial"/> where deletion begins; delete <c>Length - startIndex</c> characters.</param>
    /// <returns>False when there is no completed token to remove (empty/whitespace-only prefix).</returns>
    public static bool TryGetDeletePreviousWordStart(string textBeforePartial, out int startIndex)
    {
        startIndex = 0;
        if (string.IsNullOrEmpty(textBeforePartial))
        {
            return false;
        }

        var trimmed = textBeforePartial.TrimEnd();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var i = trimmed.Length - 1;
        while (i >= 0 && !char.IsWhiteSpace(trimmed[i]))
        {
            i--;
        }

        startIndex = i + 1;
        return textBeforePartial.Length - startIndex > 0;
    }

    /// <summary>
    /// Strips non-word characters, trims, lowercases — same rules the predictor uses for prefix matching and learning.
    /// </summary>
    public static string NormalizeWord(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var chars = input
            .Trim()
            .Where(IsWordExtendingCharacter)
            .ToArray();

        return new string(chars).Trim().ToLowerInvariant();
    }
}
