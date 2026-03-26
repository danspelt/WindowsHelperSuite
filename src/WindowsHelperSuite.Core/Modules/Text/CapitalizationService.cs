using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Modules.Text;

/// <summary>
/// Sentence-start capitalization for the writer: after . ! ? (optional closing quotes) and start of text.
/// Respects intentional casing (e.g. iPhone, eBay) and optional "i" → "I".
/// </summary>
public static class CapitalizationService
{

    /// <summary>
    /// Capitalize the first letter of each sentence in a block (paste / hotkey helper).
    /// </summary>
    public static string ApplySentenceCapitalization(string? text, WriterCapitalizationOptions options)
    {
        if (!options.Enabled || string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var chars = text.ToCharArray();
        var capitalizeNext = true;

        for (var i = 0; i < chars.Length; i++)
        {
            if (capitalizeNext)
            {
                if (char.IsLetter(chars[i]))
                {
                    if (ShouldUppercaseLetterAt(chars, i, options))
                    {
                        chars[i] = char.ToUpperInvariant(chars[i]);
                    }

                    capitalizeNext = false;
                }
            }

            if (chars[i] is '.' or '!' or '?' or '\u2026')
            {
                capitalizeNext = true;
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Fix the first word of an insertion (1–9 pick or similar) using text before the caret (includes partial removed).
    /// </summary>
    public static string FixInsertion(string? rawTextBeforeCaret, string fragment, WriterCapitalizationOptions options)
    {
        if (!options.Enabled || string.IsNullOrEmpty(fragment))
        {
            return fragment;
        }

        var trimmedEnd = fragment.TrimEnd();
        var trailing = fragment.Length > trimmedEnd.Length ? fragment[trimmedEnd.Length..] : "";

        var firstSpace = trimmedEnd.IndexOf(' ');
        if (firstSpace < 0)
        {
            return FixSingleWordAfterPrefix(rawTextBeforeCaret, trimmedEnd, options) + trailing;
        }

        var first = trimmedEnd[..firstSpace];
        var rest = trimmedEnd[firstSpace..];
        return FixSingleWordAfterPrefix(rawTextBeforeCaret, first, options) + rest + trailing;
    }

    /// <summary>
    /// Fix a word the user just finished typing (before the trailing space was applied in the target app).
    /// </summary>
    public static string FixCompletedTypedWord(string? rawTextBeforeWord, string word, WriterCapitalizationOptions options) =>
        FixSingleWordAfterPrefix(rawTextBeforeWord, word, options);

    private static string FixSingleWordAfterPrefix(string? rawTextBeforeWord, string word, WriterCapitalizationOptions options)
    {
        if (!options.Enabled || string.IsNullOrEmpty(word))
        {
            return word;
        }

        if (IsLikelyIntentionalCasing(word))
        {
            return word;
        }

        if (options.CapitalizeSingleLetterI && word == "i")
        {
            return "I";
        }

        if (ShouldCapitalizeAfterPrefix(rawTextBeforeWord))
        {
            return CapitalizeFirstLetter(word);
        }

        return word;
    }

    /// <summary>
    /// True if the word already looks intentionally cased (brands, acronyms, camelCase fragments).
    /// </summary>
    public static bool IsLikelyIntentionalCasing(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return false;
        }

        // Any uppercase after the first character → iPhone, eBay, McDonald, etc.
        for (var i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]))
            {
                return true;
            }
        }

        // All caps short token (API, OK, NASA)
        if (word.Length >= 2 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)) && word.Any(char.IsLetter))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldCapitalizeAfterPrefix(string? rawTextBeforeWord)
    {
        if (string.IsNullOrWhiteSpace(rawTextBeforeWord))
        {
            return true;
        }

        var t = rawTextBeforeWord.TrimEnd();
        while (t.Length > 0)
        {
            var last = t[^1];
            if (last is '.' or '!' or '?' or '\u2026')
            {
                return true;
            }

            if (last is '"' or '\'' or ')' or ']' or '\u2019' or '\u201d')
            {
                t = t[..^1].TrimEnd();
                continue;
            }

            return false;
        }

        return true;
    }

    private static string CapitalizeFirstLetter(string word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (!char.IsLower(c))
            {
                return word;
            }

            return word[..i] + char.ToUpperInvariant(c) + word[(i + 1)..];
        }

        return word;
    }

    /// <summary>
    /// When running full-block capitalization, skip raising a letter if it starts a word that looks intentional.
    /// </summary>
    private static bool ShouldUppercaseLetterAt(char[] chars, int i, WriterCapitalizationOptions options)
    {
        var wordLen = MeasureWordLength(chars, i);
        if (wordLen <= 0)
        {
            return true;
        }

        var word = new string(chars, i, wordLen);

        if (IsLikelyIntentionalCasing(word))
        {
            return false;
        }

        if (options.CapitalizeSingleLetterI && word.Equals("i", StringComparison.Ordinal))
        {
            return true;
        }

        return true;
    }

    private static int MeasureWordLength(char[] chars, int start)
    {
        var n = 0;
        for (var j = start; j < chars.Length; j++)
        {
            var c = chars[j];
            if (char.IsLetterOrDigit(c) || c == '\'' || c == '-')
            {
                n++;
            }
            else
            {
                break;
            }
        }

        return n;
    }
}
