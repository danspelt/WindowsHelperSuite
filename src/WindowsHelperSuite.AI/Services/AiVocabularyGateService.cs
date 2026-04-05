using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Uses an OpenAI-compatible Chat Completions endpoint to decide if a new word/phrase belongs in the word bank.
/// </summary>
public sealed class AiVocabularyGateService : IAiVocabularyGateService
{
    private const int MaxContextChars = 600;
    private readonly ILoggingService _loggingService;
    private readonly Func<AiWriterSettings> _getSettings;
    private readonly HttpClient _httpClient;

    public AiVocabularyGateService(ILoggingService loggingService, Func<AiWriterSettings> getSettings)
    {
        _loggingService = loggingService;
        _getSettings = getSettings;
        _httpClient = new HttpClient();
    }

    public async Task<bool> ShouldRememberNewItemAsync(
        string item,
        bool isPhrase,
        string? recentContext,
        CancellationToken cancellationToken = default)
    {
        var s = _getSettings();
        if (!s.EnableVocabularyGate || string.IsNullOrWhiteSpace(s.ApiKey))
        {
            return true;
        }

        var baseUrl = (s.ApiBaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        var url = $"{baseUrl}/chat/completions";
        var timeoutMs = Math.Clamp(s.VocabularyGateTimeoutMs, 800, 30_000);
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var ctx = TrimContext(recentContext);
        var userBlock =
            $"Item type: {(isPhrase ? "phrase (multiple words)" : "single word")}\n" +
            $"Item: \"{EscapeForPrompt(item)}\"\n" +
            (string.IsNullOrEmpty(ctx) ? "" : $"Recent typed context before this item (truncated): \"{EscapeForPrompt(ctx)}\"\n") +
            "\nShould this be saved to the user's personal word bank because they are likely to type it again? " +
            "Say yes for: proper names, technical terms, products, places, domain jargon, stable multi-word phrases they reuse. " +
            "Say no for: obvious typos, random characters, one-time codes/password-like strings, keyboard mashing, meaningless tokens.";

        var system =
            "You respond with a single JSON object only, no markdown, no explanation. " +
            "Schema: {\"remember\": true} or {\"remember\": false}.";

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(s.Model) ? "gpt-4o-mini" : s.Model,
            temperature = 0,
            max_tokens = 48,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = userBlock }
            }
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey.Trim());
            req.Content = JsonContent.Create(payload);

            using var resp = await _httpClient.SendAsync(req, linked.Token).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length <= 220 ? body : body[..220] + "…";
                _loggingService.Warning($"AI vocabulary gate HTTP {(int)resp.StatusCode}: {snippet}");
                return s.FallbackSaveOnAiFailure;
            }

            if (!TryParseRememberFromChatResponse(body, out var remember))
            {
                _loggingService.Warning("AI vocabulary gate: could not parse model JSON");
                return s.FallbackSaveOnAiFailure;
            }

            if (!remember)
            {
                _loggingService.Debug($"AI vocabulary gate rejected: \"{item}\"");
            }

            return remember;
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("AI vocabulary gate cancelled or timed out");
            return s.FallbackSaveOnAiFailure;
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"AI vocabulary gate error: {ex.Message}");
            return s.FallbackSaveOnAiFailure;
        }
    }

    private static string TrimContext(string? recentContext)
    {
        if (string.IsNullOrWhiteSpace(recentContext))
        {
            return string.Empty;
        }

        var t = recentContext.Trim();
        if (t.Length <= MaxContextChars)
        {
            return t;
        }

        return t[^MaxContextChars..];
    }

    private static string EscapeForPrompt(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool TryParseRememberFromChatResponse(string responseJson, out bool remember)
    {
        remember = false;
        try
        {
            using var root = JsonDocument.Parse(responseJson);
            if (!root.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            var content = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return TryParseRememberJson(content, out remember);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseRememberJson(string content, out bool remember)
    {
        remember = false;
        var trimmed = content.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            var jsonSlice = trimmed.Substring(start, end - start + 1);
            using var doc = JsonDocument.Parse(jsonSlice);
            if (!doc.RootElement.TryGetProperty("remember", out var r))
            {
                return false;
            }

            remember = r.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(r.GetString(), out var b) && b,
                JsonValueKind.Number => r.GetInt32() != 0,
                _ => false
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
