using System.Collections.Concurrent;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Enhanced AI suggestion service that combines multiple AI features
/// for smarter, context-aware writing assistance.
/// </summary>
public sealed class AiSmartSuggestionService
{
    private readonly ILoggingService _loggingService;
    private readonly IAiSuggestionService _suggestionService;
    private readonly AiSentenceCompletionService _sentenceCompletion;
    private readonly AiGrammarService _grammarService;
    private readonly AiContextService _contextService;
    private readonly Func<AiSettings> _getSettings;

    // Cache for recent suggestions to avoid redundant AI calls
    private readonly ConcurrentDictionary<string, CachedSuggestion> _suggestionCache = new();
    private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);

    public AiSmartSuggestionService(
        ILoggingService loggingService,
        IAiSuggestionService suggestionService,
        AiSentenceCompletionService sentenceCompletion,
        AiGrammarService grammarService,
        AiContextService contextService,
        Func<AiSettings> getSettings)
    {
        _loggingService = loggingService;
        _suggestionService = suggestionService;
        _sentenceCompletion = sentenceCompletion;
        _grammarService = grammarService;
        _contextService = contextService;
        _getSettings = getSettings;
    }

    /// <summary>
    /// Gets comprehensive smart suggestions including:
    /// - Next word/phrase predictions
    /// - Sentence completion
    /// - Grammar corrections
    /// - Context-aware recommendations
    /// </summary>
    public async Task<SmartSuggestionResult> GetSmartSuggestionsAsync(
        string currentText,
        string currentWord,
        string? previousSentence,
        CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        var result = new SmartSuggestionResult();

        if (!settings.EnableAiSuggestions)
        {
            return result;
        }

        // Check cache first
        var cacheKey = $"{currentText}|{currentWord}";
        if (_suggestionCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Result;
        }

        // 1. Get phrase suggestions
        var phraseTask = GetPhraseSuggestionsAsync(currentText, currentWord, previousSentence, cancellationToken);

        // 2. Get sentence completion (if at end of sentence)
        var sentenceTask = ShouldCompleteSentence(currentText)
            ? _sentenceCompletion.CompleteSentenceAsync(currentText, previousSentence, cancellationToken)
            : Task.FromResult<string?>(null);

        // 3. Check for grammar correction
        var grammarTask = currentText.Length > 10
            ? _grammarService.FixSentenceAsync(currentText, cancellationToken)
            : Task.FromResult<string?>(null);

        await Task.WhenAll(phraseTask, sentenceTask, grammarTask);

        result.PhraseSuggestions = await phraseTask ?? Array.Empty<AiSuggestionResult>();
        result.SentenceCompletion = await sentenceTask;
        result.GrammarCorrection = await grammarTask;

        // Record context for future learning
        if (!string.IsNullOrWhiteSpace(currentWord))
        {
            _contextService.RecordPhrase(currentWord);
        }

        // Cache the result
        _suggestionCache[cacheKey] = new CachedSuggestion(result, DateTime.UtcNow.Add(_cacheTtl));

        return result;
    }

    /// <summary>
    /// Gets instant word correction suggestions as the user types.
    /// </summary>
    public async Task<string?> GetInstantCorrectionAsync(
        string lastWord,
        string sentenceContext,
        CancellationToken cancellationToken = default)
    {
        // Try instant fix first (no AI call)
        if (_grammarService.TryInstantFix(lastWord, out var instantFix))
        {
            return instantFix;
        }

        // Fall back to AI correction for complex cases
        return await _grammarService.SuggestWordCorrectionAsync(lastWord, sentenceContext, cancellationToken);
    }

    /// <summary>
    /// Suggests style/tone variations of the current text.
    /// </summary>
    public Task<IReadOnlyList<StyleSuggestion>> GetStyleVariationsAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        if (!settings.EnableAiSuggestions || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return Task.FromResult<IReadOnlyList<StyleSuggestion>>(Array.Empty<StyleSuggestion>());
        }

        // This would call a style variation service
        // For now, return empty - placeholder for future enhancement
        return Task.FromResult<IReadOnlyList<StyleSuggestion>>(Array.Empty<StyleSuggestion>());
    }

    private async Task<IReadOnlyList<AiSuggestionResult>?> GetPhraseSuggestionsAsync(
        string currentText,
        string currentWord,
        string? previousSentence,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new AiSuggestionRequest
            {
                CurrentText = currentText,
                CurrentWord = currentWord,
                PreviousSentence = previousSentence,
                MaxSuggestions = 3
            };

            return await _suggestionService.GetPhraseSuggestionsAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _loggingService.Debug($"Phrase suggestions failed: {ex.Message}");
            return null;
        }
    }

    private static bool ShouldCompleteSentence(string text)
    {
        // Check if we're at a natural sentence completion point
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        // Ending with space and have reasonable length
        if (text.EndsWith(' ') && trimmed.Length > 15)
            return true;

        // Contains a verb but no clear ending
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 3 && !text.TrimEnd().EndsWithAny('.', '!', '?'))
            return true;

        return false;
    }

    private void CleanupCache()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _suggestionCache)
        {
            if (kvp.Value.Expiry < now)
            {
                _suggestionCache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private class CachedSuggestion
    {
        public SmartSuggestionResult Result { get; }
        public DateTime Expiry { get; }

        public CachedSuggestion(SmartSuggestionResult result, DateTime expiry)
        {
            Result = result;
            Expiry = expiry;
        }
    }
}

/// <summary>
/// Result from smart suggestion service containing multiple suggestion types.
/// </summary>
public class SmartSuggestionResult
{
    /// <summary>
    /// Next word/phrase suggestions from AI.
    /// </summary>
    public IReadOnlyList<AiSuggestionResult> PhraseSuggestions { get; set; } = Array.Empty<AiSuggestionResult>();

    /// <summary>
    /// Suggested completion for the current sentence.
    /// </summary>
    public string? SentenceCompletion { get; set; }

    /// <summary>
    /// Suggested grammar/spelling correction.
    /// </summary>
    public string? GrammarCorrection { get; set; }

    /// <summary>
    /// Whether any suggestions are available.
    /// </summary>
    public bool HasSuggestions => PhraseSuggestions.Count > 0 ||
                                   !string.IsNullOrEmpty(SentenceCompletion) ||
                                   !string.IsNullOrEmpty(GrammarCorrection);
}

/// <summary>
/// A style/tone variation suggestion.
/// </summary>
public class StyleSuggestion
{
    public string Label { get; set; } = "";
    public string ModifiedText { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// String extension helper for ShouldCompleteSentence.
/// </summary>
internal static class StringExtensions
{
    public static bool EndsWithAny(this string s, params char[] chars)
    {
        if (string.IsNullOrEmpty(s) || chars.Length == 0)
            return false;

        var lastChar = s[^1];
        return chars.Contains(lastChar);
    }
}
