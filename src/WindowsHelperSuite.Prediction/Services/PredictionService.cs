using System.Text.Json;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Core.Models.Writer;

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
    private readonly string _correctionsPath;
    private readonly System.Timers.Timer _saveTimer;
    private WordBankStore _store;
    private bool _dirty;

    // Fast lookup indexes rebuilt on load/seed
    private Dictionary<string, WordBankEntry> _wordIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WordBankEntry> _phraseIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WordBankEntry> _bigramIndex = new(StringComparer.OrdinalIgnoreCase);

    // Recency tracking — recently accepted words get a temporary score boost
    private readonly LinkedList<string> _recentlyAccepted = new();

    private readonly ITypingModel? _typingModel;
    private readonly Func<WriterSettings>? _getWriterSettings;

    public PredictionService(ITypingModel? typingModel = null, Func<WriterSettings>? getWriterSettings = null)
    {
        _typingModel = typingModel;
        _getWriterSettings = getWriterSettings;
        _storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "data",
            "wordbank.json");

        _correctionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "data",
            "corrections.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);

        _saveTimer = new System.Timers.Timer(SaveDebounceMs) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => FlushSave();

        _store = LoadStore();
        MergeOptionalCorrectionsFile();
        RebuildIndexes();
        EnsureSeedData();
    }

    public IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord, WriterContextSnapshot writerContext = default)
    {
        var normalizedWord = NormalizeWord(currentWord);
        var normalizedContext = NormalizePhrase(context);
        var (wordBeforeLast, lastContextWord) = GetLastTwoWords(normalizedContext);
        var contextNextWords = GetContextNextWords(lastContextWord, wordBeforeLast);

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

                // Phrase matches — always show phrases to save the most typing
                suggestions.AddRange(
                    _store.Phrases
                        .Where(entry => PhraseMatches(entry.Text, normalizedContext, normalizedWord))
                        .Select(entry => new SuggestionCandidate(
                            entry.Text,
                            BuildPhraseInsertText(entry.Text, normalizedContext, normalizedWord),
                            SuggestionKind.PhraseCompletion,
                            CalculatePhraseScore(entry, normalizedContext, normalizedWord))));

                AppendTypoCorrectionSuggestions(suggestions, normalizedWord);
                AppendPhrasePrefixBucketSuggestions(suggestions, normalizedContext, normalizedWord);
                AppendTypingModelSuggestions(suggestions, normalizedContext, normalizedWord);
            }
            else if (!string.IsNullOrWhiteSpace(lastContextWord))
            {
                // No current word typed yet, but we have context → suggest next words
                // Use trigram scoring if we have 2+ words of context
                suggestions.AddRange(GetNextWordSuggestions(lastContextWord, contextNextWords, wordBeforeLast));

                // Also show phrases that continue the context (saves the most typing)
                suggestions.AddRange(GetContextPhraseSuggestions(normalizedContext, lastContextWord));
                AppendTypingModelContextPhrases(suggestions, normalizedContext, lastContextWord);

                // If still empty, fall back to frequent words + sentence starters
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

            var scaled = suggestions
                .Select(c =>
                {
                    var baseScore = c.Score * ContextScoreMultiplier(writerContext, c.Kind);
                    var boost = _typingModel?.GetRankingBoost(c.DisplayText, c.Kind, writerContext) ?? 0;
                    return c with { Score = baseScore + boost };
                })
                .ToList();

            return scaled
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

    private double ContextScoreMultiplier(WriterContextSnapshot ctx, SuggestionKind kind)
    {
        if (kind != SuggestionKind.PhraseCompletion)
        {
            return 1.0;
        }

        var mult = ctx.Mode switch
        {
            WriterTypingMode.Chat => 1.12,
            WriterTypingMode.Email => 1.08,
            _ => 1.0
        };

        if (_getWriterSettings?.Invoke().UseWindowTitleForPhraseHints == true)
        {
            mult *= WriterTitleContextHints.PhraseBoostFromWindowTitle(ctx.ForegroundWindowTitle);
        }

        return mult;
    }

    private void AppendTypoCorrectionSuggestions(List<SuggestionCandidate> suggestions, string normalizedWord)
    {
        foreach (var c in _store.Corrections)
        {
            if (string.IsNullOrWhiteSpace(c.Typo) || string.IsNullOrWhiteSpace(c.Fix))
            {
                continue;
            }

            var typo = c.Typo.Trim();
            var fix = c.Fix.Trim();
            if (string.Equals(typo, fix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!typo.StartsWith(normalizedWord, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(fix, normalizedWord, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var exactTypo = string.Equals(normalizedWord, typo, StringComparison.OrdinalIgnoreCase);
            var score = (exactTypo ? 2800.0 : 1500.0) + Math.Min(50, Math.Max(1, c.Count)) * 3.0;
            suggestions.Add(new SuggestionCandidate(fix, fix, SuggestionKind.WordCompletion, score));
        }
    }

    private void AppendPhrasePrefixBucketSuggestions(List<SuggestionCandidate> suggestions, string normalizedContext, string normalizedWord)
    {
        foreach (var bucket in _store.PhrasePrefixes)
        {
            if (string.IsNullOrWhiteSpace(bucket.Prefix))
            {
                continue;
            }

            var prefix = bucket.Prefix.Trim();
            if (!prefix.StartsWith(normalizedWord, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var phrase in bucket.Phrases ?? [])
            {
                if (string.IsNullOrWhiteSpace(phrase))
                {
                    continue;
                }

                var trimmed = phrase.Trim();
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = 1750.0 + trimmed.Length * 10.0;
                suggestions.Add(new SuggestionCandidate(
                    trimmed,
                    BuildPhraseInsertText(trimmed, normalizedContext, normalizedWord),
                    SuggestionKind.PhraseCompletion,
                    score));
            }
        }
    }

    private void AppendTypingModelSuggestions(List<SuggestionCandidate> suggestions, string normalizedContext, string normalizedWord)
    {
        if (_typingModel == null || string.IsNullOrEmpty(normalizedWord))
        {
            return;
        }

        foreach (var c in _typingModel.GetCorrectionMatches(normalizedWord))
        {
            if (string.IsNullOrWhiteSpace(c.Corrected))
            {
                continue;
            }

            var score = 2600.0 + c.Count * 10.0;
            suggestions.Add(new SuggestionCandidate(c.Corrected, c.Corrected, SuggestionKind.WordCompletion, score));
        }

        foreach (var w in _typingModel.GetWords(normalizedWord))
        {
            if (string.IsNullOrWhiteSpace(w.Word))
            {
                continue;
            }

            var score = 920.0 + Math.Log(1 + Math.Max(1, w.Count)) * 55;
            suggestions.Add(new SuggestionCandidate(w.Word, w.Word, SuggestionKind.WordCompletion, score));
        }

        foreach (var p in _typingModel.GetPhrases(normalizedWord))
        {
            if (string.IsNullOrWhiteSpace(p.Phrase) || !PhraseMatches(p.Phrase, normalizedContext, normalizedWord))
            {
                continue;
            }

            var score = 1750.0 + p.Count * 12.0;
            suggestions.Add(new SuggestionCandidate(
                p.Phrase,
                BuildPhraseInsertText(p.Phrase, normalizedContext, normalizedWord),
                SuggestionKind.PhraseCompletion,
                score));
        }
    }

    private void AppendTypingModelContextPhrases(List<SuggestionCandidate> suggestions, string normalizedContext, string lastContextWord)
    {
        if (_typingModel == null || string.IsNullOrWhiteSpace(lastContextWord))
        {
            return;
        }

        foreach (var p in _typingModel.GetPhrases(lastContextWord))
        {
            if (string.IsNullOrWhiteSpace(p.Phrase))
            {
                continue;
            }

            var continues = p.Phrase.StartsWith(normalizedContext + " ", StringComparison.OrdinalIgnoreCase) ||
                            p.Phrase.StartsWith(lastContextWord + " ", StringComparison.OrdinalIgnoreCase);
            if (!continues)
            {
                continue;
            }

            var score = 1780.0 + p.Count * 12.0;
            suggestions.Add(new SuggestionCandidate(
                p.Phrase,
                BuildPhraseInsertText(p.Phrase, normalizedContext, string.Empty),
                SuggestionKind.PhraseCompletion,
                score));
        }
    }

    public bool WordBankContainsWord(string word)
    {
        var normalizedWord = NormalizeWord(word);
        if (normalizedWord.Length <= 1)
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _wordIndex.ContainsKey(normalizedWord);
        }
    }

    public bool WordBankContainsPhrase(string phrase)
    {
        var normalizedPhrase = NormalizePhrase(phrase);
        if (string.IsNullOrWhiteSpace(normalizedPhrase) || !normalizedPhrase.Contains(' '))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return _phraseIndex.ContainsKey(normalizedPhrase);
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

    public void AcceptWord(string word)
    {
        // Stronger learning when user explicitly picks a suggestion
        var normalizedWord = NormalizeWord(word);
        if (normalizedWord.Length <= 1) return;

        lock (_syncRoot)
        {
            if (_wordIndex.TryGetValue(normalizedWord, out var existing))
            {
                existing.Frequency += 3; // Learn faster from explicit selection
            }
            else
            {
                var entry = new WordBankEntry { Text = normalizedWord, Frequency = 5 };
                _store.Words.Add(entry);
                _wordIndex[normalizedWord] = entry;
            }

            TrackRecent(normalizedWord);
            ScheduleSave();
        }
    }

    public void AcceptPhrase(string phrase)
    {
        var normalizedPhrase = NormalizePhrase(phrase);
        if (string.IsNullOrWhiteSpace(normalizedPhrase) || !normalizedPhrase.Contains(' ')) return;

        lock (_syncRoot)
        {
            if (_phraseIndex.TryGetValue(normalizedPhrase, out var existing))
            {
                existing.Frequency += 3;
            }
            else
            {
                var entry = new WordBankEntry { Text = normalizedPhrase, Frequency = 5 };
                _store.Phrases.Add(entry);
                _phraseIndex[normalizedPhrase] = entry;
            }

            ScheduleSave();
        }
    }

    public void RemoveSuggestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var normalizedWord = NormalizeWord(text);
        var normalizedPhrase = NormalizePhrase(text);

        lock (_syncRoot)
        {
            // Try to remove as word first
            if (_wordIndex.TryGetValue(normalizedWord, out var wordEntry))
            {
                _store.Words.Remove(wordEntry);
                _wordIndex.Remove(normalizedWord);
                ScheduleSave();
                return;
            }

            // Try to remove as phrase
            if (_phraseIndex.TryGetValue(normalizedPhrase, out var phraseEntry))
            {
                _store.Phrases.Remove(phraseEntry);
                _phraseIndex.Remove(normalizedPhrase);
                ScheduleSave();
                return;
            }
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
        LearnBigramWithContext(null, previousWord, currentWord);
    }

    /// <summary>
    /// Dreamlike: Learn bigrams with optional context word before.
    /// When wordBefore is provided, also learns the trigram "wordBefore previousWord currentWord".
    /// </summary>
    public void LearnBigramWithContext(string? wordBefore, string previousWord, string currentWord)
    {
        var prev = NormalizeWord(previousWord);
        var curr = NormalizeWord(currentWord);
        if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(curr))
        {
            return;
        }

        lock (_syncRoot)
        {
            // Learn bigram
            var key = $"{prev} {curr}";
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

            // Also learn trigram if we have context
            if (!string.IsNullOrWhiteSpace(wordBefore))
            {
                var wb = NormalizeWord(wordBefore);
                if (!string.IsNullOrWhiteSpace(wb))
                {
                    var trigramKey = $"{wb} {prev} {curr}";
                    if (_bigramIndex.TryGetValue(trigramKey, out var triExisting))
                    {
                        triExisting.Frequency = Math.Min(triExisting.Frequency + 1, 200);
                    }
                    else if (_store.Bigrams.Count < MaxBigrams)
                    {
                        var triEntry = new WordBankEntry { Text = trigramKey, Frequency = 1 };
                        _store.Bigrams.Add(triEntry);
                        _bigramIndex[trigramKey] = triEntry;
                    }
                }
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

        try
        {
            var json = File.ReadAllText(_storagePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new WordBankStore();
            }

            return JsonSerializer.Deserialize<WordBankStore>(json, _jsonOptions) ?? new WordBankStore();
        }
        catch (JsonException)
        {
            TryBackupCorruptWordBank(_storagePath);
            return new WordBankStore();
        }
        catch (IOException)
        {
            return new WordBankStore();
        }
    }

    private static void TryBackupCorruptWordBank(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var dest = Path.Combine(dir, $"{name}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Copy(path, dest, overwrite: false);
        }
        catch
        {
            // best-effort backup only
        }
    }

    /// <summary>Merges optional %AppData%/WindowsHelperSuite/data/corrections.json into the in-memory store (same shape as <see cref="CorrectionsFile"/>).</summary>
    private void MergeOptionalCorrectionsFile()
    {
        if (!File.Exists(_correctionsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_correctionsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var file = JsonSerializer.Deserialize<CorrectionsFile>(json, _jsonOptions);
            if (file?.Corrections == null)
            {
                return;
            }

            foreach (var c in file.Corrections)
            {
                if (string.IsNullOrWhiteSpace(c.Typo) || string.IsNullOrWhiteSpace(c.Fix))
                {
                    continue;
                }

                var duplicate = _store.Corrections.Exists(x =>
                    string.Equals(x.Typo, c.Typo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Fix, c.Fix, StringComparison.OrdinalIgnoreCase));
                if (!duplicate)
                {
                    _store.Corrections.Add(c);
                }
            }
        }
        catch
        {
            // Ignore malformed optional file
        }
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

    private IEnumerable<SuggestionCandidate> GetNextWordSuggestions(string lastContextWord, HashSet<string> contextNextWords, string wordBeforeLast = "")
    {
        // Suggest words that commonly follow the previous word
        foreach (var nextWord in contextNextWords)
        {
            if (_wordIndex.TryGetValue(nextWord, out var entry))
            {
                var rank = GetBigramRank(lastContextWord, entry.Text);
                var trigramBoost = 0;

                // TRIGRAM BONUS: If we have 2 words of context, check if this completes a trigram
                if (!string.IsNullOrWhiteSpace(wordBeforeLast))
                {
                    var trigramRank = GetTrigramRank(wordBeforeLast, lastContextWord, entry.Text);
                    if (trigramRank < 50) // Found a trigram match
                    {
                        trigramBoost = 800 - (trigramRank * 25); // Significant boost for trigram matches
                    }
                }

                var score = 2000 - (rank * 30) + (entry.Frequency * 5) + trigramBoost;
                yield return new SuggestionCandidate(
                    entry.Text,
                    entry.Text,
                    SuggestionKind.NextWord,
                    score);
            }
        }
    }

    private int GetTrigramRank(string word1, string word2, string word3)
    {
        // Check learned trigrams (stored as "word1 word2 word3" in bigram index)
        var key = $"{word1} {word2} {word3}";
        if (_bigramIndex.TryGetValue(key, out var learned))
        {
            return Math.Max(0, 3 - Math.Min(learned.Frequency, 3)); // Learned trigrams rank very high
        }

        return 50; // No trigram match
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

    private IEnumerable<SuggestionCandidate> GetContextPhraseSuggestions(string normalizedContext, string lastContextWord)
    {
        // Show phrases that continue from the current context — saves the most keystrokes
        return _store.Phrases
            .Where(entry =>
            {
                var phrase = entry.Text;
                // Phrase starts with context (e.g., context="how" → "how are you")
                if (phrase.StartsWith(normalizedContext + " ", StringComparison.OrdinalIgnoreCase))
                    return true;
                // Phrase starts with last context word (e.g., lastContextWord="i" → "i would like")
                if (phrase.StartsWith(lastContextWord + " ", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            })
            .Select(entry =>
            {
                // Calculate how many chars this phrase saves vs typing it out
                var charsSaved = entry.Text.Length - lastContextWord.Length;
                var savingsBoost = charsSaved * 15; // Big boost for phrases that save more typing
                var frequencyScore = entry.Frequency >= 5 ? entry.Frequency * 18 : entry.Frequency * 12;
                return new SuggestionCandidate(
                    entry.Text,
                    BuildPhraseInsertText(entry.Text, normalizedContext, string.Empty),
                    SuggestionKind.PhraseCompletion,
                    1800 + savingsBoost + frequencyScore + GetRecencyBoost(entry.Text));
            });
    }

    private HashSet<string> GetContextNextWords(string lastContextWord, string wordBeforeLast = "")
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(lastContextWord))
        {
            return result;
        }

        // TRIGRAM: Check "wordBeforeLast + lastContextWord" first for stronger context
        if (!string.IsNullOrWhiteSpace(wordBeforeLast))
        {
            var trigramKey = $"{wordBeforeLast} {lastContextWord}";
            // Static trigram map (if we had one) would go here
            // For now, boost learned bigrams that match the trigram pattern
            foreach (var bigram in _store.Bigrams.Where(b => b.Text.StartsWith(trigramKey + " ", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = bigram.Text.Split(' ', 3);
                if (parts.Length >= 3)
                {
                    result.Add(parts[2]); // Third word in trigram
                }
            }
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

    private static (string Word1, string Word2) GetLastTwoWords(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return (string.Empty, string.Empty);
        }

        var parts = context.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[^2], parts[^1]);
        }
        else if (parts.Length == 1)
        {
            return (string.Empty, parts[0]);
        }
        return (string.Empty, string.Empty);
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
        // Big boost for phrases — they save the most keystrokes
        var charsSaved = Math.Max(0, entry.Text.Length - combined.Length);
        var savingsBoost = charsSaved * 12;
        var recencyBoost = GetRecencyBoost(entry.Text);
        var wordCount = entry.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var clarityBoost = wordCount switch
        {
            <= 2 => 30,
            <= 5 => 140,
            <= 7 => 70,
            _ => -80
        };
        return exactBoost + prefixBoost + frequencyScore + savingsBoost + recencyBoost + clarityBoost;
    }

    private static string BuildPhraseInsertText(string phrase, string context, string currentWord)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }

        var combined = string.IsNullOrWhiteSpace(context)
            ? currentWord.Trim()
            : string.IsNullOrWhiteSpace(currentWord)
                ? context.Trim()
                : $"{context} {currentWord}".Trim();

        if (string.IsNullOrWhiteSpace(combined))
        {
            return phrase;
        }

        if (!phrase.StartsWith(combined, StringComparison.OrdinalIgnoreCase))
        {
            return phrase;
        }

        if (phrase.Length == combined.Length)
        {
            return phrase;
        }

        var remainder = phrase[combined.Length..];
        return remainder.TrimStart();
    }

    private static string NormalizeWord(string input) => WriterWordBufferPolicy.NormalizeWord(input);

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
        public List<CorrectionEntry> Corrections { get; set; } = [];
        public List<PhrasePrefixBucket> PhrasePrefixes { get; set; } = [];
    }

    private sealed class WordBankEntry
    {
        public string Text { get; set; } = string.Empty;
        public int Frequency { get; set; }
    }
}
