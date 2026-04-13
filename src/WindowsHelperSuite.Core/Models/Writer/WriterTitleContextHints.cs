namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>
/// Opt-in phrase ranking boost from the foreground window title (browser mail tabs, etc.).
/// Does not log titles; only substring checks, case-insensitive.
/// </summary>
public static class WriterTitleContextHints
{
    /// <summary>
    /// Multiplier applied to phrase base score when title hints are enabled in settings.
    /// 1.0 when no recognizable hint.
    /// </summary>
    public static double PhraseBoostFromWindowTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return 1.0;
        }

        var t = title.AsSpan();
        if (ContainsOrdinalIgnoreCase(t, "outlook"))
        {
            return 1.06;
        }

        if (ContainsOrdinalIgnoreCase(t, "gmail") || ContainsOrdinalIgnoreCase(t, "mail - "))
        {
            return 1.05;
        }

        if (ContainsOrdinalIgnoreCase(t, "inbox") && ContainsOrdinalIgnoreCase(t, "@"))
        {
            return 1.04;
        }

        return 1.0;
    }

    private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> haystack, string needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).Equals(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
