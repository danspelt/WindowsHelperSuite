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
