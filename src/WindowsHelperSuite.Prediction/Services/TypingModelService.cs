using System.Text.Json;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Prediction.Services;

/// <summary>Persists personal words/phrases/corrections to JSON (default: %AppData%/WindowsHelperSuite/writer-model.json).</summary>
public sealed class TypingModelService : ITypingModel, IDisposable
{
    private const int SaveDebounceMs = 7000;
    private const int MaxWords = 15_000;
    private const int MaxPhrases = 4_000;
    private const int MaxCorrections = 1_000;

    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly System.Timers.Timer _saveTimer;
    private TypingModelStore _store = new();
    private bool _dirty;

    private Dictionary<string, TypingWordEntry> _wordIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TypingCorrectionRecord> _correctionIndex = new(StringComparer.OrdinalIgnoreCase);

    public TypingModelService(string? storagePath = null)
    {
        _path = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "writer-model.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        _saveTimer = new System.Timers.Timer(SaveDebounceMs) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => FlushSave();

        Load();
    }

    public void RecordWord(string word, WriterContextSnapshot context)
    {
        var w = NormalizeToken(word);
        if (w.Length <= 1)
        {
            return;
        }

        var ctxKey = ContextKey(context.Mode);
        lock (_sync)
        {
            if (_wordIndex.TryGetValue(w, out var entry))
            {
                entry.Count++;
                entry.LastUsed = DateTime.UtcNow;
                IncrementContext(entry.ContextCounts, ctxKey);
            }
            else
            {
                entry = new TypingWordEntry
                {
                    Word = w,
                    Count = 1,
                    LastUsed = DateTime.UtcNow,
                    ContextCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [ctxKey] = 1 }
                };
                _store.Words.Add(entry);
                _wordIndex[w] = entry;
            }

            TrimIfNeeded();
            ScheduleSave();
        }
    }

    public void RecordPhrase(string phrase, WriterContextSnapshot context)
    {
        var p = NormalizePhrase(phrase);
        if (!ShouldRecordPhrase(p))
        {
            return;
        }

        var first = GetFirstWord(p);
        if (string.IsNullOrEmpty(first))
        {
            return;
        }

        var ctxKey = ContextKey(context.Mode);
        lock (_sync)
        {
            var existing = _store.Phrases.Find(x => string.Equals(x.Phrase, p, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Count++;
                existing.LastUsed = DateTime.UtcNow;
                IncrementContext(existing.ContextCounts, ctxKey);
            }
            else
            {
                var entry = new TypingPhraseEntry
                {
                    Phrase = p,
                    Prefix = first,
                    Count = 1,
                    LastUsed = DateTime.UtcNow,
                    ContextCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [ctxKey] = 1 }
                };
                _store.Phrases.Add(entry);
            }

            TrimIfNeeded();
            ScheduleSave();
        }
    }

    public void RecordCorrection(string typed, string corrected)
    {
        var t = NormalizeToken(typed);
        var c = NormalizeToken(corrected);
        if (t.Length == 0 || c.Length == 0 || string.Equals(t, c, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            if (_correctionIndex.TryGetValue(t, out var rec))
            {
                if (!string.Equals(rec.Corrected, c, StringComparison.OrdinalIgnoreCase))
                {
                    // Keep stronger target if conflict — prefer higher count path
                    rec.Corrected = c;
                }

                rec.Count++;
            }
            else
            {
                rec = new TypingCorrectionRecord { Typed = t, Corrected = c, Count = 1 };
                _store.Corrections.Add(rec);
                _correctionIndex[t] = rec;
            }

            TrimIfNeeded();
            ScheduleSave();
        }
    }

    public IReadOnlyList<TypingWordEntry> GetWords(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return [];
        }

        var p = NormalizeToken(prefix);
        lock (_sync)
        {
            return _store.Words
                .Where(x => x.Word.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.LastUsed)
                .Take(48)
                .ToList();
        }
    }

    public IReadOnlyList<TypingPhraseEntry> GetPhrases(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return [];
        }

        var p = NormalizeToken(prefix);
        lock (_sync)
        {
            return _store.Phrases
                .Where(x =>
                    x.Prefix.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    x.Phrase.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.LastUsed)
                .Take(48)
                .ToList();
        }
    }

    public IReadOnlyList<TypingCorrectionRecord> GetCorrectionMatches(string typedPrefix)
    {
        if (string.IsNullOrEmpty(typedPrefix))
        {
            return [];
        }

        var p = NormalizeToken(typedPrefix);
        lock (_sync)
        {
            return _store.Corrections
                .Where(x => x.Typed.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Count)
                .Take(24)
                .ToList();
        }
    }

    public string? GetCorrection(string typed)
    {
        var t = NormalizeToken(typed);
        if (t.Length == 0)
        {
            return null;
        }

        lock (_sync)
        {
            return _correctionIndex.TryGetValue(t, out var r) ? r.Corrected : null;
        }
    }

    public double GetRankingBoost(string displayText, SuggestionKind kind, WriterContextSnapshot ctx)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return 0;
        }

        var key = NormalizePhrase(displayText);
        var ctxName = ContextKey(ctx.Mode);
        lock (_sync)
        {
            if (kind == SuggestionKind.PhraseCompletion || displayText.Contains(' ', StringComparison.Ordinal))
            {
                var pe = _store.Phrases.Find(x => string.Equals(x.Phrase, key, StringComparison.OrdinalIgnoreCase));
                if (pe != null)
                {
                    return ComputeBoost(pe.Count, pe.LastUsed, pe.ContextCounts, ctxName);
                }
            }
            else
            {
                var we = _wordIndex.GetValueOrDefault(NormalizeToken(displayText));
                if (we != null)
                {
                    return ComputeBoost(we.Count, we.LastUsed, we.ContextCounts, ctxName);
                }
            }
        }

        return 0;
    }

    private static double ComputeBoost(int count, DateTime lastUsed, Dictionary<string, int> contextCounts, string ctxName)
    {
        var freq = Math.Log(1 + Math.Max(1, count)) * 42;
        var days = (DateTime.UtcNow - lastUsed).TotalDays;
        var recency = days <= 7 ? 90 : days <= 30 ? 40 : 0;
        var ctx = 1.0;
        if (contextCounts.TryGetValue(ctxName, out var cc) && cc > 0)
        {
            ctx = 1.0 + Math.Min(0.35, cc * 0.02);
        }

        return (freq + recency) * ctx;
    }

    public void Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _store = new TypingModelStore();
                    RebuildIndexes();
                    return;
                }

                var json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _store = new TypingModelStore();
                }
                else
                {
                    _store = JsonSerializer.Deserialize<TypingModelStore>(json, _json) ?? new TypingModelStore();
                }
            }
            catch
            {
                _store = new TypingModelStore();
            }

            RebuildIndexes();
        }
    }

    public void Save()
    {
        FlushSave();
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        if (!_dirty)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                var json = JsonSerializer.Serialize(_store, _json);
                File.WriteAllText(_path, json);
                _dirty = false;
            }
            catch
            {
                // Non-fatal
            }
        }
    }

    private void RebuildIndexes()
    {
        _wordIndex = new Dictionary<string, TypingWordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _store.Words)
        {
            if (!string.IsNullOrWhiteSpace(w.Word))
            {
                _wordIndex[w.Word] = w;
            }
        }

        _correctionIndex = new Dictionary<string, TypingCorrectionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _store.Corrections)
        {
            if (!string.IsNullOrWhiteSpace(c.Typed))
            {
                _correctionIndex[c.Typed] = c;
            }
        }
    }

    private void TrimIfNeeded()
    {
        while (_store.Words.Count > MaxWords)
        {
            var victim = _store.Words.OrderBy(x => x.Count).ThenBy(x => x.LastUsed).First();
            _store.Words.Remove(victim);
            _wordIndex.Remove(victim.Word);
        }

        while (_store.Phrases.Count > MaxPhrases)
        {
            var victim = _store.Phrases.OrderBy(x => x.Count).ThenBy(x => x.LastUsed).First();
            _store.Phrases.Remove(victim);
        }

        while (_store.Corrections.Count > MaxCorrections)
        {
            var victim = _store.Corrections.OrderBy(x => x.Count).First();
            _store.Corrections.Remove(victim);
            _correctionIndex.Remove(victim.Typed);
        }
    }

    private static void IncrementContext(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var n);
        dict[key] = n + 1;
    }

    private static string ContextKey(WriterTypingMode mode) => mode switch
    {
        WriterTypingMode.Chat => "Chat",
        WriterTypingMode.Email => "Email",
        WriterTypingMode.Development => "Development",
        _ => "Neutral"
    };

    private static string NormalizeToken(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var chars = input
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            .ToArray();

        return new string(chars).ToLowerInvariant();
    }

    private static string NormalizePhrase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var compact = string.Join(' ', input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return compact.ToLowerInvariant();
    }

    private static string GetFirstWord(string phrase)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : NormalizeToken(parts[0]);
    }

    private static bool ShouldRecordPhrase(string phrase)
    {
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 10)
        {
            return false;
        }

        if (parts.All(w => w.All(ch => char.IsDigit(ch) || (!char.IsLetterOrDigit(ch) && ch != '\'' && ch != '-'))))
        {
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        FlushSave();
        _saveTimer.Dispose();
    }

    private sealed class TypingModelStore
    {
        public List<TypingWordEntry> Words { get; set; } = [];
        public List<TypingPhraseEntry> Phrases { get; set; } = [];
        public List<TypingCorrectionRecord> Corrections { get; set; } = [];
    }
}
