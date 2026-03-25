using System.Text.Json;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Prediction.Services;

public class PredictionService : IPredictionService, IDisposable
{
    private const int MaxSuggestions = 9;
    private const int MaxBigrams = 5000;
    private const int SaveDebounceMs = 3000;
    private const int RecencyWindowSize = 10;

    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _storagePath;
    private readonly System.Timers.Timer _saveTimer;
    private WordBankStore _store;
    private bool _dirty;

    // Fast lookup indexes rebuilt on load/seed
    private Dictionary<string, WordBankEntry> _wordIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WordBankEntry> _phraseIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WordBankEntry> _bigramIndex = new(StringComparer.OrdinalIgnoreCase);

    // Recency tracking — recently accepted words get a temporary score boost
    private readonly LinkedList<string> _recentlyAccepted = new();

    public PredictionService()
    {
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "data",
            "wordbank.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);

        _saveTimer = new System.Timers.Timer(SaveDebounceMs) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => FlushSave();

        _store = LoadStore();
        RebuildIndexes();
        EnsureSeedData();
    }

    public IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord)
    {
        var normalizedWord = NormalizeWord(currentWord);
        var normalizedContext = NormalizePhrase(context);
        var lastContextWord = GetLastWord(normalizedContext);
        var contextNextWords = GetContextNextWords(lastContextWord);

        lock (_syncRoot)
        {
            var suggestions = new List<SuggestionCandidate>();

            if (!string.IsNullOrWhiteSpace(normalizedWord))
            {
                // Word prefix matches, boosted by context
                suggestions.AddRange(
                    _store.Words
                        .Where(entry => entry.Text.StartsWith(normalizedWord, StringComparison.OrdinalIgnoreCase))
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            entry.Text,
                            SuggestionKind.WordCompletion,
                            CalculateWordScore(entry, normalizedWord, contextNextWords))));

                // Fallback to contains match
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

                // Phrase matches
                suggestions.AddRange(
                    _store.Phrases
                        .Where(entry => PhraseMatches(entry.Text, normalizedContext, normalizedWord))
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            entry.Text,
                            SuggestionKind.PhraseCompletion,
                            CalculatePhraseScore(entry, normalizedContext, normalizedWord))));
            }
            else if (!string.IsNullOrWhiteSpace(lastContextWord))
            {
                // No current word typed yet, but we have context → suggest next words
                suggestions.AddRange(GetNextWordSuggestions(lastContextWord, contextNextWords));

                // If no bigrams found, fall back to frequent words + sentence starters
                if (suggestions.Count == 0)
                {
                    suggestions.AddRange(GetFrequentWordSuggestions());
                    suggestions.AddRange(GetSentenceStarterSuggestions());
                }
            }
            else
            {
                // No context at all → suggest sentence starters + frequent words
                suggestions.AddRange(GetSentenceStarterSuggestions());
                suggestions.AddRange(GetFrequentWordSuggestions());
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
        if (normalizedWord.Length <= 1)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_wordIndex.TryGetValue(normalizedWord, out var existing))
            {
                existing.Frequency++;
            }
            else
            {
                var entry = new WordBankEntry { Text = normalizedWord, Frequency = 1 };
                _store.Words.Add(entry);
                _wordIndex[normalizedWord] = entry;
            }

            TrackRecent(normalizedWord);
            ScheduleSave();
        }
    }

    private void TrackRecent(string word)
    {
        _recentlyAccepted.Remove(word);
        _recentlyAccepted.AddFirst(word);
        while (_recentlyAccepted.Count > RecencyWindowSize)
            _recentlyAccepted.RemoveLast();
    }

    private double GetRecencyBoost(string word)
    {
        var node = _recentlyAccepted.First;
        int position = 0;
        while (node != null)
        {
            if (node.Value.Equals(word, StringComparison.OrdinalIgnoreCase))
                return (RecencyWindowSize - position) * 40; // Most recent = 400 boost, decaying
            node = node.Next;
            position++;
        }
        return 0;
    }

    public void LearnBigram(string previousWord, string currentWord)
    {
        var prev = NormalizeWord(previousWord);
        var curr = NormalizeWord(currentWord);
        if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(curr))
        {
            return;
        }

        var key = $"{prev} {curr}";
        lock (_syncRoot)
        {
            if (_bigramIndex.TryGetValue(key, out var existing))
            {
                existing.Frequency = Math.Min(existing.Frequency + 1, 200);
            }
            else
            {
                // Evict lowest-frequency bigram if at capacity
                if (_store.Bigrams.Count >= MaxBigrams)
                {
                    var weakest = _store.Bigrams.OrderBy(b => b.Frequency).First();
                    _store.Bigrams.Remove(weakest);
                    _bigramIndex.Remove(weakest.Text);
                }

                var entry = new WordBankEntry { Text = key, Frequency = 1 };
                _store.Bigrams.Add(entry);
                _bigramIndex[key] = entry;
            }

            ScheduleSave();
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
            if (_phraseIndex.TryGetValue(normalizedPhrase, out var existing))
            {
                existing.Frequency++;
            }
            else
            {
                var entry = new WordBankEntry { Text = normalizedPhrase, Frequency = 1 };
                _store.Phrases.Add(entry);
                _phraseIndex[normalizedPhrase] = entry;
            }

            ScheduleSave();
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

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        if (!_dirty) return;
        lock (_syncRoot)
        {
            try
            {
                var json = JsonSerializer.Serialize(_store, _jsonOptions);
                File.WriteAllText(_storagePath, json);
                _dirty = false;
            }
            catch { /* Don't crash on file I/O errors */ }
        }
    }

    private void RebuildIndexes()
    {
        _wordIndex = new Dictionary<string, WordBankEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _store.Words)
        {
            _wordIndex.TryAdd(entry.Text, entry);
        }

        _phraseIndex = new Dictionary<string, WordBankEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _store.Phrases)
        {
            _phraseIndex.TryAdd(entry.Text, entry);
        }

        _bigramIndex = new Dictionary<string, WordBankEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _store.Bigrams)
        {
            _bigramIndex.TryAdd(entry.Text, entry);
        }
    }

    private void EnsureSeedData()
    {
        lock (_syncRoot)
        {
            var existingWords = new HashSet<string>(
                _store.Words.Select(entry => entry.Text),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (words, baseFrequency) in EnglishDictionary.WordTiers)
            {
                foreach (var word in words)
                {
                    if (!existingWords.Contains(word))
                    {
                        _store.Words.Add(new WordBankEntry { Text = word, Frequency = baseFrequency });
                        existingWords.Add(word);
                    }
                }
            }

            var existingPhrases = new HashSet<string>(
                _store.Phrases.Select(entry => entry.Text),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (phrases, baseFrequency) in EnglishDictionary.PhraseTiers)
            {
                foreach (var phrase in phrases)
                {
                    if (!existingPhrases.Contains(phrase))
                    {
                        _store.Phrases.Add(new WordBankEntry { Text = phrase, Frequency = baseFrequency });
                        existingPhrases.Add(phrase);
                    }
                }
            }

            FlushSave();
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

    private IEnumerable<SuggestionCandidate> GetNextWordSuggestions(string lastContextWord, HashSet<string> contextNextWords)
    {
        // Suggest words that commonly follow the previous word
        foreach (var nextWord in contextNextWords)
        {
            if (_wordIndex.TryGetValue(nextWord, out var entry))
            {
                var rank = GetBigramRank(lastContextWord, entry.Text);
                var score = 2000 - (rank * 30) + (entry.Frequency * 5);
                yield return new SuggestionCandidate(
                    entry.Text,
                    entry.Text,
                    SuggestionKind.WordCompletion,
                    score);
            }
        }
    }

    private static readonly HashSet<string> _starterSet = new(EnglishDictionary.SentenceStarters, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<SuggestionCandidate> GetSentenceStarterSuggestions()
    {
        foreach (var starter in EnglishDictionary.SentenceStarters)
        {
            if (_wordIndex.TryGetValue(starter, out var entry))
            {
                var rank = Array.IndexOf(EnglishDictionary.SentenceStarters, starter);
                var score = 1500 - (rank >= 0 ? rank * 20 : 500) + (entry.Frequency * 3);
                yield return new SuggestionCandidate(
                    entry.Text,
                    entry.Text,
                    SuggestionKind.WordCompletion,
                    score);
            }
        }
    }

    private IEnumerable<SuggestionCandidate> GetFrequentWordSuggestions()
    {
        // Return the most frequently used words as fallback
        return _store.Words
            .Where(w => w.Text.Length >= 2)
            .OrderByDescending(w => w.Frequency)
            .Take(MaxSuggestions * 2)
            .Select(entry => new SuggestionCandidate(
                entry.Text,
                entry.Text,
                SuggestionKind.WordCompletion,
                800 + (entry.Frequency * 5) + GetRecencyBoost(entry.Text)));
    }

    private HashSet<string> GetContextNextWords(string lastContextWord)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(lastContextWord))
        {
            return result;
        }

        // Static bigram map
        if (EnglishDictionary.NextWordMap.TryGetValue(lastContextWord, out var staticNext))
        {
            foreach (var word in staticNext)
            {
                result.Add(word);
            }
        }

        // Learned bigrams
        var prefix = lastContextWord + " ";
        foreach (var bigram in _store.Bigrams.Where(b => b.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var parts = bigram.Text.Split(' ', 2);
            if (parts.Length == 2)
            {
                result.Add(parts[1]);
            }
        }

        return result;
    }

    private int GetBigramRank(string previousWord, string nextWord)
    {
        // Check learned bigrams first (user patterns rank highest)
        var key = $"{previousWord} {nextWord}";
        if (_bigramIndex.TryGetValue(key, out var learned))
        {
            return Math.Max(0, 5 - Math.Min(learned.Frequency, 5));
        }

        // Check static bigram map position
        if (EnglishDictionary.NextWordMap.TryGetValue(previousWord, out var staticNext))
        {
            var idx = Array.FindIndex(staticNext, s => s.Equals(nextWord, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                return idx + 5;
            }
        }

        return 50;
    }

    private static string GetLastWord(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return string.Empty;
        }

        var parts = context.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : string.Empty;
    }

    private double CalculateWordScore(WordBankEntry entry, string currentWord, HashSet<string> contextNextWords)
    {
        var exactBoost = entry.Text.Equals(currentWord, StringComparison.OrdinalIgnoreCase) ? 1000 : 0;
        var prefixBoost = entry.Text.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase) ? 250 : 0;
        var contextBoost = contextNextWords.Contains(entry.Text) ? 500 : 0;
        var recencyBoost = GetRecencyBoost(entry.Text);
        // Favor shorter completions (closer to what user typed)
        var remainingChars = Math.Max(0, entry.Text.Length - currentWord.Length);
        var lengthPenalty = remainingChars * 2.0;
        // Short common words get a bonus
        var shortWordBonus = entry.Text.Length <= 5 ? 50 : 0;
        // High-frequency words the user has taught get a strong boost
        var frequencyScore = entry.Frequency >= 10 ? entry.Frequency * 15 : entry.Frequency * 10;
        return exactBoost + prefixBoost + contextBoost + recencyBoost + shortWordBonus + frequencyScore - lengthPenalty;
    }

    private double CalculateContainsScore(WordBankEntry entry, string currentWord)
    {
        var index = entry.Text.IndexOf(currentWord, StringComparison.OrdinalIgnoreCase);
        var positionPenalty = index < 0 ? 100 : index * 5;
        var recencyBoost = GetRecencyBoost(entry.Text);
        return recencyBoost + (entry.Frequency * 6) - positionPenalty;
    }

    private double CalculatePhraseScore(WordBankEntry entry, string context, string currentWord)
    {
        var combined = string.IsNullOrWhiteSpace(context) ? currentWord : $"{context} {currentWord}".Trim();
        var exactBoost = entry.Text.Equals(combined, StringComparison.OrdinalIgnoreCase) ? 1200 : 0;
        var prefixBoost = entry.Text.StartsWith(combined, StringComparison.OrdinalIgnoreCase) ? 400 : 0;
        // Phrases that the user types frequently should rank higher
        var frequencyScore = entry.Frequency >= 5 ? entry.Frequency * 18 : entry.Frequency * 12;
        return exactBoost + prefixBoost + frequencyScore;
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

    public void Dispose()
    {
        _saveTimer.Stop();
        FlushSave();
        _saveTimer.Dispose();
    }

    private sealed record SuggestionCandidate(string DisplayText, string InsertText, SuggestionKind Kind, double Score);

    private sealed class WordBankStore
    {
        public List<WordBankEntry> Words { get; set; } = [];
        public List<WordBankEntry> Phrases { get; set; } = [];
        public List<WordBankEntry> Bigrams { get; set; } = [];
    }

    private sealed class WordBankEntry
    {
        public string Text { get; set; } = string.Empty;
        public int Frequency { get; set; }
    }
}
