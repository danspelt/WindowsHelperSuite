using System.Text.Json;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Prediction.Services;

public class PredictionService : IPredictionService
{
    private const int MaxSuggestions = 9;
    private static readonly string[] SeedWords =
    [
        "the", "be", "to", "of", "and", "a", "in", "that", "have", "i",
        "it", "for", "not", "on", "with", "he", "as", "you", "do", "at",
        "this", "but", "his", "by", "from", "they", "we", "say", "her", "she",
        "or", "an", "will", "my", "one", "all", "would", "there", "their", "what",
        "so", "up", "out", "if", "about", "who", "get", "which", "go", "me"
    ];

    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _storagePath;
    private WordBankStore _store;

    public PredictionService()
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "data",
            "wordbank.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
        _store = LoadStore();
        EnsureSeedWords();
    }

    public IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord)
    {
        var normalizedWord = NormalizeWord(currentWord);
        var normalizedContext = NormalizePhrase(context);

        lock (_syncRoot)
        {
            var suggestions = new List<SuggestionCandidate>();

            if (!string.IsNullOrWhiteSpace(normalizedWord))
            {
                suggestions.AddRange(
                    _store.Words
                        .Where(entry => entry.Text.StartsWith(normalizedWord, StringComparison.OrdinalIgnoreCase))
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            entry.Text,
                            SuggestionKind.WordCompletion,
                            CalculateWordScore(entry, normalizedWord))));

                if (suggestions.Count == 0)
                {
                    suggestions.AddRange(
                        _store.Words
                            .Where(entry => entry.Text.Contains(normalizedWord, StringComparison.OrdinalIgnoreCase))
                            .Select(entry => new SuggestionCandidate(
                                entry.Text,
                                entry.Text,
                                SuggestionKind.WordCompletion,
                                CalculateContainsScore(entry, normalizedWord))));
                }

                suggestions.AddRange(
                    _store.Phrases
                        .Where(entry => PhraseMatches(entry.Text, normalizedContext, normalizedWord))
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            entry.Text,
                            SuggestionKind.PhraseCompletion,
                            CalculatePhraseScore(entry, normalizedContext, normalizedWord))));
            }

            if (suggestions.Count == 0 && string.IsNullOrWhiteSpace(normalizedWord))
            {
                suggestions.AddRange(
                    _store.Words
                        .OrderByDescending(entry => entry.Frequency)
                        .ThenBy(entry => entry.Text, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxSuggestions)
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            entry.Text,
                            SuggestionKind.WordCompletion,
                            entry.Frequency)));
            }

            return suggestions
                .GroupBy(candidate => candidate.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Score).First())
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Take(MaxSuggestions)
                .Select((candidate, index) => new SuggestionItem
                {
                    Slot = index + 1,
                    DisplayText = candidate.DisplayText,
                    InsertText = candidate.InsertText,
                    Kind = candidate.Kind,
                    Score = candidate.Score
                })
                .ToList();
        }
    }

    public void LearnWord(string word)
    {
        var normalizedWord = NormalizeWord(word);
        if (normalizedWord.Length <= 2)
        {
            return;
        }

        lock (_syncRoot)
        {
            var existing = _store.Words.FirstOrDefault(entry => entry.Text.Equals(normalizedWord, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _store.Words.Add(new WordBankEntry { Text = normalizedWord, Frequency = 1 });
            }
            else
            {
                existing.Frequency++;
            }

            SaveStore();
        }
    }

    public void LearnPhrase(string phrase)
    {
        var normalizedPhrase = NormalizePhrase(phrase);
        if (string.IsNullOrWhiteSpace(normalizedPhrase))
        {
            return;
        }

        if (!normalizedPhrase.Contains(' '))
        {
            LearnWord(normalizedPhrase);
            return;
        }

        lock (_syncRoot)
        {
            var existing = _store.Phrases.FirstOrDefault(entry => entry.Text.Equals(normalizedPhrase, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _store.Phrases.Add(new WordBankEntry { Text = normalizedPhrase, Frequency = 1 });
            }
            else
            {
                existing.Frequency++;
            }

            SaveStore();
        }
    }

    private WordBankStore LoadStore()
    {
        if (!File.Exists(_storagePath))
        {
            return new WordBankStore();
        }

        var json = File.ReadAllText(_storagePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WordBankStore();
        }

        return JsonSerializer.Deserialize<WordBankStore>(json, _jsonOptions) ?? new WordBankStore();
    }

    private void SaveStore()
    {
        var json = JsonSerializer.Serialize(_store, _jsonOptions);
        File.WriteAllText(_storagePath, json);
    }

    private void EnsureSeedWords()
    {
        lock (_syncRoot)
        {
            foreach (var word in SeedWords)
            {
                if (_store.Words.All(entry => !entry.Text.Equals(word, StringComparison.OrdinalIgnoreCase)))
                {
                    _store.Words.Add(new WordBankEntry { Text = word, Frequency = 1 });
                }
            }

            SaveStore();
        }
    }

    private static bool PhraseMatches(string phrase, string context, string currentWord)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            var combined = string.IsNullOrWhiteSpace(currentWord) ? context : $"{context} {currentWord}";
            return phrase.StartsWith(combined, StringComparison.OrdinalIgnoreCase);
        }

        return phrase.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase);
    }

    private static double CalculateWordScore(WordBankEntry entry, string currentWord)
    {
        var exactBoost = entry.Text.Equals(currentWord, StringComparison.OrdinalIgnoreCase) ? 1000 : 0;
        var prefixBoost = entry.Text.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase) ? 250 : 0;
        var lengthPenalty = Math.Max(0, entry.Text.Length - currentWord.Length) * 0.5;
        return exactBoost + prefixBoost + (entry.Frequency * 10) - lengthPenalty;
    }

    private static double CalculateContainsScore(WordBankEntry entry, string currentWord)
    {
        var index = entry.Text.IndexOf(currentWord, StringComparison.OrdinalIgnoreCase);
        var positionPenalty = index < 0 ? 100 : index * 5;
        return (entry.Frequency * 6) - positionPenalty;
    }

    private static double CalculatePhraseScore(WordBankEntry entry, string context, string currentWord)
    {
        var combined = string.IsNullOrWhiteSpace(context) ? currentWord : $"{context} {currentWord}".Trim();
        var exactBoost = entry.Text.Equals(combined, StringComparison.OrdinalIgnoreCase) ? 1200 : 0;
        var prefixBoost = entry.Text.StartsWith(combined, StringComparison.OrdinalIgnoreCase) ? 400 : 0;
        return exactBoost + prefixBoost + (entry.Frequency * 12);
    }

    private static string NormalizeWord(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var chars = input
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            .ToArray();

        return new string(chars).Trim().ToLowerInvariant();
    }

    private static string NormalizePhrase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var compact = string.Join(' ', input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment)));

        return compact.ToLowerInvariant();
    }

    private sealed record SuggestionCandidate(string DisplayText, string InsertText, SuggestionKind Kind, double Score);

    private sealed class WordBankStore
    {
        public List<WordBankEntry> Words { get; set; } = [];
        public List<WordBankEntry> Phrases { get; set; } = [];
    }

    private sealed class WordBankEntry
    {
        public string Text { get; set; } = string.Empty;
        public int Frequency { get; set; }
    }
}
