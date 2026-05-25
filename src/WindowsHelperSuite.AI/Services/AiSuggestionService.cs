using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// OpenAI-compatible chat completions for writer overlay phrase suggestions.
/// </summary>
public sealed class AiSuggestionService : IAiSuggestionService, IDisposable
{
    private static readonly Regex HorizontalWhitespaceRun = new("[ \\t\\u00A0]{2,}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILoggingService _loggingService;
    private readonly Func<AiSettings> _getSettings;
    private readonly HttpClient _httpClient = new();

    public AiSuggestionService(ILoggingService loggingService, Func<AiSettings> getSettings)
    {
        _loggingService = loggingService;
        _getSettings = getSettings;
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<IReadOnlyList<AiSuggestionResult>> GetPhraseSuggestionsAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        if (!settings.EnableAiSuggestions || !settings.EnableAiPhraseCompletion)
        {
            return Array.Empty<AiSuggestionResult>();
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _loggingService.Debug("Overlay AI: no API key, skipping");
            return Array.Empty<AiSuggestionResult>();
        }

        using var timeoutCts = new CancellationTokenSource(settings.AiTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var suggestions = await GetSuggestionsFromAiAsync(request, settings, linkedCts.Token);
            return suggestions.Take(Math.Clamp(settings.MaxAiSuggestions, 1, 9)).ToList();
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("Overlay AI request timed out or was cancelled");
            return Array.Empty<AiSuggestionResult>();
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Overlay AI failed: {ex.Message}");
            return Array.Empty<AiSuggestionResult>();
        }
    }

    public async Task<IReadOnlyList<string>> GetQuickCompletionsAsync(
        string currentText,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        return Array.Empty<string>();
    }

    private async Task<IReadOnlyList<AiSuggestionResult>> GetSuggestionsFromAiAsync(
        AiSuggestionRequest request,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        var max = Math.Clamp(request.MaxSuggestions > 0 ? request.MaxSuggestions : settings.MaxAiSuggestions, 1, 9);
        var fullLine = request.CurrentText?.Trim() ?? "";
        if (fullLine.Length > 1200)
        {
            fullLine = fullLine[^1200..];
        }

        var system = $"""
            You complete text for a Windows typing-assist overlay. The user has cerebral palsy with dysarthric (slurred) speech, so their input may have atypical word choices. Read the FULL line so far to understand intended grammar and meaning.

            Return ONLY the next fragment to type. Do NOT repeat any substring from their line. Prefer the shortest natural continuation (usually one word; a few words only if grammar requires it). Each line under 80 characters.

            Consider the user's speech patterns when predicting - they may type words that sound similar to their intended words. Pick the most contextually appropriate completion.

            Output exactly one suggestion per line: no numbers, bullets, quotes, labels, or explanations. At most {max} lines total. If nothing fits, output exactly: NONE
            """;

        var user = new StringBuilder();
        user.AppendLine($"Return up to {max} distinct continuations (one per line).");
        if (!string.IsNullOrWhiteSpace(request.PreviousCompletedWord)
            && string.IsNullOrWhiteSpace(request.CurrentWord))
        {
            user.AppendLine($"The user just finished the word \"{request.PreviousCompletedWord.Trim()}\" and needs the NEXT word or short phrase.");
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentWord))
        {
            user.AppendLine($"Partial word being typed (may be empty): \"{request.CurrentWord.Trim()}\"");
        }

        user.AppendLine("Full line so far:");
        user.AppendLine(fullLine.Length > 0 ? fullLine : "(empty)");

        var model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model.Trim();
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user.ToString() }
            },
            temperature = 0.25,
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
            _loggingService.Warning($"Overlay AI HTTP {(int)res.StatusCode}: {Truncate(body, 200)}");
            return Array.Empty<AiSuggestionResult>();
        }

        string content;
        try
        {
            using var doc = JsonDocument.Parse(body);
            content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim() ?? "";
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Overlay AI bad JSON: {ex.Message}");
            return Array.Empty<AiSuggestionResult>();
        }

        return ParseSuggestionLines(content, max);
    }

    private static IReadOnlyList<AiSuggestionResult> ParseSuggestionLines(string content, int max)
    {
        var results = new List<AiSuggestionResult>();
        foreach (var rawLine in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = StripListPrefix(rawLine).Trim();
            if (line.Length == 0 || line.Length > 120)
            {
                continue;
            }

            if (line.StartsWith('"') && line.EndsWith('"') && line.Length >= 2)
            {
                line = line[1..^1].Trim();
            }

            if (string.Equals(line, "NONE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(line, "NONE.", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            line = HorizontalWhitespaceRun.Replace(line, " ");

            var isPhrase = line.Contains(' ', StringComparison.Ordinal);
            results.Add(new AiSuggestionResult
            {
                Text = line,
                Confidence = 0.85,
                IsPhraseCompletion = isPhrase
            });

            if (results.Count >= max)
            {
                break;
            }
        }

        return results;
    }

    private static string StripListPrefix(string line)
    {
        var s = line.Trim();
        if (s.Length >= 2 && s[0] == '-' && char.IsWhiteSpace(s[1]))
        {
            return s[2..].TrimStart();
        }

        if (s.Length >= 2 && s[0] == '•' && char.IsWhiteSpace(s[1]))
        {
            return s[2..].TrimStart();
        }

        var i = 0;
        while (i < s.Length && char.IsDigit(s[i]))
        {
            i++;
        }

        if (i > 0 && i < s.Length && s[i] is '.' or ')')
        {
            return s[(i + 1)..].TrimStart();
        }

        return s;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
