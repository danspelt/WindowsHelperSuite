using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// AI-powered sentence completion service.
/// Completes entire sentences based on context and user's writing style.
/// </summary>
public sealed class AiSentenceCompletionService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILoggingService _loggingService;
    private readonly Func<AiSettings> _getSettings;
    private readonly HttpClient _httpClient = new();

    public AiSentenceCompletionService(ILoggingService loggingService, Func<AiSettings> getSettings)
    {
        _loggingService = loggingService;
        _getSettings = getSettings;
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Completes the current sentence based on context.
    /// </summary>
    public async Task<string?> CompleteSentenceAsync(
        string currentText,
        string? contextBefore,
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
            return await GetSentenceCompletionAsync(currentText, contextBefore, settings, linked.Token);
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("Sentence completion timed out");
            return null;
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Sentence completion failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> GetSentenceCompletionAsync(
        string currentText,
        string? contextBefore,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        var fullContext = BuildContext(currentText, contextBefore);
        if (fullContext.Length > 800)
        {
            fullContext = fullContext[^800..];
        }

        var system = """
            You are a sentence completion assistant for a user with cerebral palsy.
            Given partial text, provide a NATURAL completion to finish the current sentence.
            
            Rules:
            - Return ONLY the completion text (the part that comes after the input)
            - Do NOT repeat any text from the input
            - Keep completions under 20 words
            - Match the user's tone and style
            - If the sentence seems complete, return "COMPLETE"
            - Be helpful and natural, not robotic
            """;

        var user = $"""
            Complete this sentence naturally:
            
            Current text: "{fullContext}"
            
            Completion (just the ending):
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
            temperature = 0.3,
            max_tokens = 64
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
            _loggingService.Debug($"Sentence completion HTTP {(int)res.StatusCode}");
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

        if (string.IsNullOrWhiteSpace(content) ||
            content.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Clean up: remove quotes if present
        if (content.StartsWith('"') && content.EndsWith('"') && content.Length >= 2)
        {
            content = content[1..^1].Trim();
        }

        return content;
    }

    private static string BuildContext(string currentText, string? contextBefore)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(contextBefore))
        {
            sb.Append(contextBefore.Trim());
            if (!contextBefore.EndsWith(' '))
            {
                sb.Append(' ');
            }
        }
        sb.Append(currentText.Trim());
        return sb.ToString();
    }
}
