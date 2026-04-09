using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Prediction;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>Quick words/phrases: defaults, clone, ordering for menus, import/export JSON.</summary>
public static class QuickTextSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolveSpeakText(string text, string? speakText) =>
        string.IsNullOrWhiteSpace(speakText) ? text : speakText.Trim();

    public static QuickTextSettings Clone(QuickTextSettings source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<QuickTextSettings>(json, JsonOptions) ?? new QuickTextSettings();
    }

    public static string Serialize(QuickTextSettings settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    public static QuickTextSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<QuickTextSettings>(json, JsonOptions) ?? new QuickTextSettings();

    /// <summary>After load: fix empty ids, seed if both lists empty.</summary>
    public static void NormalizeAndSeedIfEmpty(QuickTextSettings s)
    {
        NormalizeIds(s);
        if (s.Words.Count == 0 && s.Phrases.Count == 0)
        {
            ResetToFactoryDefaults(s);
        }
        else
        {
            RepairSortOrders(s);
        }
    }

    /// <summary>After manual edits or import: fix ids and contiguous sort order without adding defaults.</summary>
    public static void RepairAfterImport(QuickTextSettings s)
    {
        NormalizeIds(s);
        RepairSortOrders(s);
    }

    private static void RepairSortOrders(QuickTextSettings s)
    {
        NormalizeSortOrders(s.Words, w => w.SortOrder, (w, v) => w.SortOrder = v);
        NormalizeSortOrders(s.Phrases, p => p.SortOrder, (p, v) => p.SortOrder = v);
    }

    public static void ResetToFactoryDefaults(QuickTextSettings s)
    {
        s.Words = CreateDefaultWords();
        s.Phrases = CreateDefaultPhrases();
        NormalizeIds(s);
    }

    private static void NormalizeIds(QuickTextSettings s)
    {
        foreach (var w in s.Words)
        {
            if (w.Id == Guid.Empty)
            {
                w.Id = Guid.NewGuid();
            }
        }

        foreach (var p in s.Phrases)
        {
            if (p.Id == Guid.Empty)
            {
                p.Id = Guid.NewGuid();
            }
        }
    }

    private static void NormalizeSortOrders<T>(List<T> items, Func<T, int> getOrder, Action<T, int> setOrder)
    {
        var ordered = items.OrderBy(getOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            setOrder(ordered[i], i);
        }
    }

    /// <summary>Enabled words: favorites first, then <see cref="QuickWordItem.SortOrder"/>, then text.</summary>
    public static IReadOnlyList<QuickWordItem> GetOrderedEnabledWords(QuickTextSettings s) =>
        s.Words
            .Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.IsFavorite)
            .ThenBy(w => w.SortOrder)
            .ThenBy(w => w.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<QuickPhraseItem> GetOrderedEnabledPhrases(QuickTextSettings s) =>
        s.Phrases
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Text))
            .OrderByDescending(p => p.IsFavorite)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<QuickWordItem> CreateDefaultWords()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<QuickWordItem>();
        var sort = 0;

        foreach (var (words, _) in EnglishDictionary.WordTiers)
        {
            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word) || !seen.Add(word))
                    continue;

                // Title-case the display text
                var display = char.ToUpper(word[0]) + word[1..];
                list.Add(new QuickWordItem
                {
                    Id = Guid.NewGuid(),
                    Text = display,
                    SpeakText = null,
                    IsEnabled = true,
                    IsFavorite = false,
                    SortOrder = sort++,
                });
            }
        }

        return list;
    }

    private static List<QuickPhraseItem> CreateDefaultPhrases()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<QuickPhraseItem>();
        var sort = 0;

        foreach (var (phrases, _) in EnglishDictionary.PhraseTiers)
        {
            foreach (var phrase in phrases)
            {
                if (string.IsNullOrWhiteSpace(phrase) || !seen.Add(phrase))
                    continue;

                // Sentence-case the display text
                var display = char.ToUpper(phrase[0]) + phrase[1..];
                list.Add(new QuickPhraseItem
                {
                    Id = Guid.NewGuid(),
                    Text = display,
                    SpeakText = null,
                    IsEnabled = true,
                    IsFavorite = false,
                    SortOrder = sort++,
                });
            }
        }

        return list;
    }
}
