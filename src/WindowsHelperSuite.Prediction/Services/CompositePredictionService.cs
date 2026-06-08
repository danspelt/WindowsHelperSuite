using System.Net.Http;
using CoreInterfaces = WindowsHelperSuite.Core.Interfaces;
using CoreWriter = WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Llm;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Storage;

namespace WindowsHelperSuite.Prediction.Services;

/// <summary>
/// Routes overlay suggestions through the modular <c>WindowsHelperSuite.Writer</c> engine while keeping
/// word-bank persistence and learning on the existing <see cref="PredictionService"/> implementation.
/// </summary>
public sealed class CompositePredictionService : CoreInterfaces.IPredictionService, IDisposable
{
    private const int PostSpaceNextWordQuota = 4;

    private readonly PredictionService _wordBank;
    private readonly global::WindowsHelperSuite.Writer.Abstractions.IPredictionService _writerEngine;
    private readonly JsonTypingModelStore _typingStores;
    private readonly JsonUserLanguageModelStore _languageStore;
    private readonly HttpClient _httpClient;
    private readonly Func<CoreWriter.WriterContextSnapshot> _getWriterSnapshot;
    private readonly Func<WriterSettings>? _getWriterSettings;
    private bool _disposed;

    public IUserLanguageModelStore UserLanguageStore => _languageStore;

    public CompositePredictionService(
        CoreInterfaces.ITypingModel typingModel,
        Func<WriterSettings>? getWriterSettings,
        Func<CoreWriter.WriterContextSnapshot> getWriterSnapshot,
        LocalLlmOptions? localLlmOptions = null,
        HttpClient? httpClient = null)
    {
        _getWriterSnapshot = getWriterSnapshot;
        _getWriterSettings = getWriterSettings;
        _httpClient = httpClient ?? new HttpClient();
        _typingStores = new JsonTypingModelStore();
        _languageStore = new JsonUserLanguageModelStore(_typingStores);
        _wordBank = new PredictionService(typingModel, getWriterSettings);
        var llm = localLlmOptions ?? new LocalLlmOptions();
        var nextLookup = new WordBankNextWordLookup(_wordBank);
        _writerEngine = global::WindowsHelperSuite.Writer.Services.WriterPredictionBootstrap.CreateDefaultEngine(
            typingModel,
            _typingStores,
            llm,
            _httpClient,
            nextWordLookup: nextLookup);
    }

    public void NotifySentenceCommitted(string sentence)
    {
        var ctx = WriterEngineSnapshotMapper.ToWriterEngine(_getWriterSnapshot());
        _ = _languageStore.RecordCommittedTextAsync(sentence, ctx);
    }

    public IReadOnlyList<SuggestionItem> GetSuggestions(
        string context,
        string currentWord,
        CoreWriter.WriterContextSnapshot writerContext = default)
    {
        var token = currentWord ?? "";
        var maxSlots = Math.Clamp(_getWriterSettings?.Invoke().MaxSuggestions ?? 9, 3, 15);
        var sentence = string.IsNullOrWhiteSpace(context)
            ? token
            : string.IsNullOrWhiteSpace(token)
                ? context.Trim()
                : $"{context.TrimEnd()} {token}".Trim();

        var full = sentence;
        var engineCtx = WriterEngineSnapshotMapper.ToWriterEngine(writerContext);
        var previousCompleted = WriterSentenceContext.LastCompletedWord(context ?? "");
        var request = new PredictionRequest
        {
            FullText = full,
            CurrentSentence = sentence,
            PreviousCompletedWord = previousCompleted,
            CurrentToken = token,
            CaretIndex = full.Length,
            Context = engineCtx,
            MaxSuggestions = maxSlots,
            PreferLocalOnly = false
        };

        var result = _writerEngine.PredictAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        var engineItems = MapEngineToSuggestionItems(result, context ?? "", token, maxSlots);
        var wordBankItems = _wordBank.GetSuggestions(context ?? "", token, writerContext);
        var postSpace = string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(previousCompleted);
        return MergeSuggestionLists(engineItems, wordBankItems, maxSlots, postSpace);
    }

    /// <summary>Merges engine and word-bank lists; after Space, word-bank next-word hits are prioritized.</summary>
    public static List<SuggestionItem> MergeSuggestionLists(
        List<SuggestionItem> engineItems,
        IReadOnlyList<SuggestionItem> wordBank,
        int maxSlots,
        bool postSpace)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<SuggestionItem>();

        static bool IsNextWordSlot(SuggestionItem s) =>
            s.Kind == SuggestionKind.NextWord
            || (!s.DisplayText.Contains(' ', StringComparison.Ordinal)
                && s.Kind is SuggestionKind.WordCompletion or SuggestionKind.UserHistory);

        void AddUnique(IEnumerable<SuggestionItem> items, bool fromEngine, double scoreScale = 1.0)
        {
            foreach (var s in items)
            {
                var d = s.DisplayText.Trim();
                if (d.Length == 0 || seen.Contains(d))
                {
                    continue;
                }

                seen.Add(d);
                merged.Add(new SuggestionItem
                {
                    DisplayText = s.DisplayText,
                    InsertText = s.InsertText,
                    Kind = s.Kind,
                    Score = (fromEngine ? s.Score : s.Score * 0.92) * scoreScale
                });

                if (merged.Count >= maxSlots)
                {
                    return;
                }
            }
        }

        if (postSpace)
        {
            var bankNext = wordBank.Where(IsNextWordSlot).OrderByDescending(s => s.Score);
            var bankOther = wordBank.Where(s => !IsNextWordSlot(s)).OrderByDescending(s => s.Score);
            var engineNext = engineItems.Where(IsNextWordSlot).OrderByDescending(s => s.Score);
            var engineOther = engineItems.Where(s => !IsNextWordSlot(s)).OrderByDescending(s => s.Score);

            AddUnique(bankNext, fromEngine: false);
            AddUnique(engineNext.Take(Math.Max(0, PostSpaceNextWordQuota - merged.Count)), fromEngine: true);
            AddUnique(bankOther, fromEngine: false);
            AddUnique(engineOther, fromEngine: true);
            AddUnique(engineNext, fromEngine: true, scoreScale: 0.9);
        }
        else
        {
            AddUnique(engineItems, fromEngine: true);
            AddUnique(wordBank, fromEngine: false);
        }

        for (var i = 0; i < merged.Count; i++)
        {
            merged[i].Slot = i + 1;
        }

        return merged;
    }

    private static List<SuggestionItem> MapEngineToSuggestionItems(
        PredictionResult result,
        string context,
        string token,
        int maxSlots)
    {
        var list = new List<SuggestionItem>();
        var slot = 1;
        foreach (var c in result.Suggestions)
        {
            if (slot > maxSlots)
            {
                break;
            }

            var display = CleanSuggestionText(c.Text);
            if (display.Length == 0)
            {
                continue;
            }

            var isPhrase = c.IsPhrase || display.Contains(' ', StringComparison.Ordinal);
            var insert = isPhrase
                ? BuildPhraseInsertText(display, NormalizePhrase(context), CoreWriter.WriterWordBufferPolicy.NormalizeWord(token))
                : BuildWordInsertText(display, token);

            SuggestionKind kind;
            if (c.Source.Contains("local-llm", StringComparison.OrdinalIgnoreCase))
            {
                kind = SuggestionKind.AiSuggestion;
            }
            else if (c.Source.Contains("next-word", StringComparison.OrdinalIgnoreCase))
            {
                kind = string.IsNullOrWhiteSpace(token) ? SuggestionKind.NextWord : SuggestionKind.WordCompletion;
            }
            else if (c.Source.Contains("phrase-memory", StringComparison.OrdinalIgnoreCase) ||
                     c.Source.Contains("recency", StringComparison.OrdinalIgnoreCase))
            {
                kind = isPhrase ? SuggestionKind.PhraseCompletion : SuggestionKind.UserHistory;
            }
            else
            {
                kind = isPhrase ? SuggestionKind.PhraseCompletion : SuggestionKind.WordCompletion;
            }

            list.Add(new SuggestionItem
            {
                Slot = slot++,
                DisplayText = display,
                InsertText = insert,
                Kind = kind,
                Score = Math.Max(1, c.FinalScore * 120)
            });
        }

        return list;
    }

    private static readonly System.Text.RegularExpressions.Regex _leadingMarker =
        new(@"^[\d]+[.)\-:]\s*", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _multiSpace =
        new(@"[ \t\u00A0]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string CleanSuggestionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        s = _leadingMarker.Replace(s, "");
        s = s.TrimStart('-', '•', '*', '·', '"', '\'', '`').Trim();
        s = _multiSpace.Replace(s, " ").Trim();
        s = s.TrimEnd(',', ';');
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 6) s = string.Join(" ", words[..6]);
        if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s[1..];
        return s;
    }

    public bool WordBankContainsWord(string word) => _wordBank.WordBankContainsWord(word);

    public bool WordBankContainsPhrase(string phrase) => _wordBank.WordBankContainsPhrase(phrase);

    public void LearnWord(string word) => _wordBank.LearnWord(word);

    public void LearnPhrase(string phrase) => _wordBank.LearnPhrase(phrase);

    public void LearnBigram(string previousWord, string currentWord) => _wordBank.LearnBigram(previousWord, currentWord);

    public void LearnBigramWithContext(string? wordBefore, string previousWord, string currentWord) =>
        _wordBank.LearnBigramWithContext(wordBefore, previousWord, currentWord);

    public void AcceptWord(string word)
    {
        _wordBank.AcceptWord(word);
        var ctx = WriterEngineSnapshotMapper.ToWriterEngine(_getWriterSnapshot());
        _ = _languageStore.RecordAcceptedSuggestionAsync(word, ctx);
    }

    public void AcceptPhrase(string phrase)
    {
        _wordBank.AcceptPhrase(phrase);
        var ctx = WriterEngineSnapshotMapper.ToWriterEngine(_getWriterSnapshot());
        _ = _languageStore.RecordAcceptedSuggestionAsync(phrase, ctx);
    }

    public void RemoveSuggestion(string text)
    {
        _wordBank.RemoveSuggestion(text);
    }

    public void CleanupNonsensicalEntries()
    {
        _wordBank.CleanupNonsensicalEntries();
    }

    public void ClearAll()
    {
        _wordBank.ClearAll();
        _languageStore.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wordBank.Dispose();
        _languageStore.Dispose();
        _httpClient.Dispose();
    }

    private static string BuildWordInsertText(string fullWord, string currentToken)
    {
        var w = fullWord.Trim();
        var t = CoreWriter.WriterWordBufferPolicy.NormalizeWord(currentToken);
        if (t.Length > 0 && w.StartsWith(t, StringComparison.OrdinalIgnoreCase) && w.Length > t.Length)
        {
            return w[t.Length..];
        }

        return w;
    }

    private static string BuildPhraseInsertText(string phrase, string normalizedContext, string normalizedWord)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }

        var combined = string.IsNullOrWhiteSpace(normalizedContext)
            ? normalizedWord.Trim()
            : string.IsNullOrWhiteSpace(normalizedWord)
                ? normalizedContext.Trim()
                : $"{normalizedContext} {normalizedWord}".Trim();

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

    private static string NormalizePhrase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return string.Join(' ', input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment))).ToLowerInvariant();
    }
}
