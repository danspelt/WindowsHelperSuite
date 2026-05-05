using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// AI-powered grammar and spelling correction service.
/// Provides instant fixes for common errors and dysarthric speech patterns.
/// </summary>
public sealed class AiGrammarService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILoggingService _loggingService;
    private readonly Func<AiSettings> _getSettings;
    private readonly HttpClient _httpClient = new();

    // Common patterns that don't need AI - instant local fixes
    private static readonly Dictionary<string, string> InstantFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teh"] = "the",
        ["adn"] = "and",
        ["nad"] = "and",
        ["taht"] = "that",
        ["wiht"] = "with",
        ["fo"] = "for",
        ["ot"] = "to",
        ["si"] = "is",
        ["ti"] = "it",
        ["eh"] = "he",
        ["se"] = "she",
        ["eh "] = "he ",
        ["se "] = "she ",
    };

    public AiGrammarService(ILoggingService loggingService, Func<AiSettings> getSettings)
    {
        _loggingService = loggingService;
        _getSettings = getSettings;
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Checks if a quick local fix is available (no AI call needed).
    /// </summary>
    public bool TryInstantFix(string word, out string? fixedWord)
    {
        // Check for exact match first
        if (InstantFixes.TryGetValue(word, out var fix))
        {
            fixedWord = fix;
            return true;
        }

        // Check for partial match at end of word
        foreach (var kvp in InstantFixes)
        {
            if (word.EndsWith(" " + kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                // Replace the ending
                var search = kvp.Key.Trim();
                var replace = kvp.Value;
                fixedWord = word[..word.LastIndexOf(search, StringComparison.OrdinalIgnoreCase)] + replace;
                return true;
            }
        }

        fixedWord = null;
        return false;
    }

    /// <summary>
    /// Fixes grammar and spelling in a sentence using AI.
    /// </summary>
    public async Task<string?> FixSentenceAsync(
        string sentence,
        CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        if (!settings.EnableAiSuggestions || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return null;
        }

        using var timeoutCts = new CancellationTokenSource(settings.AiTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await GetGrammarFixAsync(sentence, settings, linked.Token);
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("Grammar fix timed out");
            return null;
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Grammar fix failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if the last word typed might be a typo and suggests a correction.
    /// </summary>
    public async Task<string?> SuggestWordCorrectionAsync(
        string lastWord,
        string sentenceContext,
        CancellationToken cancellationToken = default)
    {
        // Try instant fix first
        if (TryInstantFix(lastWord, out var instantFix))
        {
            return instantFix;
        }

        var settings = _getSettings();
        if (!settings.EnableAiSuggestions || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return null;
        }

        // Skip short words and common words
        if (lastWord.Length <= 2 || IsCommonWord(lastWord))
        {
            return null;
        }

        using var timeoutCts = new CancellationTokenSource(Math.Min(settings.AiTimeoutMs, 300));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await GetWordCorrectionAsync(lastWord, sentenceContext, settings, linked.Token);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GetGrammarFixAsync(string sentence, AiSettings settings, CancellationToken cancellationToken)
    {
        if (sentence.Length > 500)
        {
            sentence = sentence[^500..];
        }

        var system = """
            You correct grammar, spelling, and word choice for a user with cerebral palsy.
            The user may have speech patterns that affect their typing.
            
            Rules:
            - Fix only obvious errors
            - Keep the user's intended meaning
            - Don't change style or tone unnecessarily
            - Return ONLY the corrected sentence
            - If no changes needed, return "OK"
            """;

        var user = $"""
            Fix this sentence:
            "{sentence}"
            
            Corrected:
            """;

        var model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model.Trim();
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = 0.2,
            max_tokens = 128
        };

        var baseUrl = (settings.ApiBaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey!.Trim());

        using var res = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        string? content;
        try
        {
            using var doc = JsonDocument.Parse(body);
            content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(content) || content.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Remove quotes if present
        if (content.StartsWith('"') && content.EndsWith('"') && content.Length >= 2)
        {
            content = content[1..^1].Trim();
        }

        // Don't return if identical
        if (content.Equals(sentence, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return content;
    }

    private async Task<string?> GetWordCorrectionAsync(
        string lastWord,
        string sentenceContext,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        var system = """
            You check if a word is spelled correctly or if it matches the intended word based on context.
            The user has speech patterns that may cause phonetic spellings.
            
            Rules:
            - If the word looks like a phonetic misspelling, suggest the correct word
            - Consider the sentence context
            - Return ONLY the corrected word, or "OK" if no correction needed
            - Be conservative - only fix obvious errors
            """;

        var user = $"""
            Context: "{sentenceContext}"
            Last word typed: "{lastWord}"
            
            Is this the intended word? If not, what should it be?
            
            Answer (word only or "OK"):
            """;

        var model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model.Trim();
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = 0.1,
            max_tokens = 16
        };

        var baseUrl = (settings.ApiBaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey!.Trim());

        using var res = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        string? content;
        try
        {
            using var doc = JsonDocument.Parse(body);
            content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(content) || content.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Remove quotes and extra whitespace
        content = content.Trim('"', ' ', '\t', '\n', '\r');

        // Don't suggest if same as input
        if (content.Equals(lastWord, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return content;
    }

    private static bool IsCommonWord(string word)
    {
        var commonWords = new[] { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did", "will", "would", "could", "should", "may", "might", "can", "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them" };
        return commonWords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }
}
