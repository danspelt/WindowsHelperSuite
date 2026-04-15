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
    /// Letters can wake the session; digits, symbols, and punctuation do not; see <c>InputService.OnKeyPressed</c>.
    /// </summary>
    public static bool CanStartWriterSessionFromKeystroke(char ch) =>
        char.IsLetter(ch);

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

    /// <summary>
    /// Splits <paramref name="sentenceTrimmedEnd"/> (no trailing whitespace) into the prefix before the last token and that token.
    /// </summary>
    public static void SplitLastWhitespaceToken(string sentenceTrimmedEnd, out string prefixBeforeLast, out string lastToken)
    {
        prefixBeforeLast = string.Empty;
        lastToken = string.Empty;
        if (string.IsNullOrEmpty(sentenceTrimmedEnd))
        {
            return;
        }

        var i = sentenceTrimmedEnd.Length - 1;
        while (i >= 0 && !char.IsWhiteSpace(sentenceTrimmedEnd[i]))
        {
            i--;
        }

        if (i < 0)
        {
            lastToken = sentenceTrimmedEnd;
            return;
        }

        lastToken = sentenceTrimmedEnd[(i + 1)..];
        prefixBeforeLast = sentenceTrimmedEnd[..i].TrimEnd();
    }

    /// <summary>
    /// Resolves the word committed on space/punctuation when <paramref name="currentWord"/> may be only a retyped suffix
    /// while <paramref name="sentenceTrimmedEnd"/> already holds the full last token (mid-word repair).
    /// </summary>
    public static void TryResolveCompletedWordForCommit(
        string sentenceTrimmedEnd,
        string currentWord,
        string fallbackTextBeforeWord,
        out string wordCompleted,
        out string textBeforeWord)
    {
        SplitLastWhitespaceToken(sentenceTrimmedEnd, out var prefixBeforeLast, out var lastToken);

        if (lastToken.Length == 0)
        {
            wordCompleted = currentWord;
            textBeforeWord = fallbackTextBeforeWord;
            return;
        }

        if (!string.Equals(lastToken, currentWord, StringComparison.OrdinalIgnoreCase)
            && lastToken.EndsWith(currentWord, StringComparison.OrdinalIgnoreCase))
        {
            wordCompleted = lastToken;
            textBeforeWord = prefixBeforeLast;
            return;
        }

        if (string.Equals(lastToken, currentWord, StringComparison.OrdinalIgnoreCase))
        {
            wordCompleted = lastToken;
            textBeforeWord = prefixBeforeLast;
            return;
        }

        wordCompleted = currentWord;
        textBeforeWord = fallbackTextBeforeWord;
    }

    /// <summary>
    /// True when <paramref name="sentence"/> ends with <paramref name="currentWord"/> and removing one character should
    /// trim both buffers: after a whitespace boundary, when the sentence is exactly the word, or when the character before
    /// the suffix is word-extending (same token at the end — e.g. sentence <c>match</c> and partial <c>h</c> after fixing <c>matc</c>).
    /// </summary>
    public static bool IsSentenceSuffixAlignedWithCurrentWord(string sentence, string currentWord)
    {
        if (currentWord.Length == 0 || sentence.Length < currentWord.Length)
        {
            return false;
        }

        if (!sentence.AsSpan(sentence.Length - currentWord.Length).SequenceEqual(currentWord.AsSpan()))
        {
            return false;
        }

        if (sentence.Length == currentWord.Length)
        {
            return true;
        }

        var beforeSuffix = sentence[sentence.Length - currentWord.Length - 1];
        if (char.IsWhiteSpace(beforeSuffix))
        {
            return true;
        }

        // Same whitespace-delimited token at sentence end (not only "space before partial")
        return IsWordExtendingCharacter(beforeSuffix);
    }
}
