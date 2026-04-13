using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StillSpace.Writer;

/// <summary>OpenAI chat calls for the writing assistant only (next-word hints). Separate from counselor/TTS clients in the app.</summary>
public sealed class WriterAssistantClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Delay after last draft change before requesting a hint (matches view-model debounce).</summary>
    public const int DefaultDebounceMilliseconds = 520;

    /// <summary>Per-request ceiling for the hint API call.</summary>
    public const int DefaultRequestTimeoutSeconds = 14;

    public void Dispose() => _http.Dispose();

    public string? ResolveApiKey(IWriterOpenAiSettings settings)
    {
        var k = settings.OpenAiApiKey?.Trim();
        if (!string.IsNullOrEmpty(k)) return k;
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    }

    public string ResolveHintModel(IWriterOpenAiSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiHintModel)
            ? settings.OpenAiHintModel.Trim()
            : (Environment.GetEnvironmentVariable("OPENAI_HINT_MODEL")?.Trim() ?? "gpt-4o-mini");

    /// <summary>Neutral writing continuation for in-progress dictation—not counselor chat.</summary>
    public async Task<(bool Ok, string Text, string Error)> PredictNextWordsAsync(
        IWriterOpenAiSettings settings,
        string partialUserText,
        CancellationToken cancellationToken = default)
    {
        var key = ResolveApiKey(settings);
        if (string.IsNullOrEmpty(key))
            return (true, "", "");

        var trimmed = partialUserText.Trim();
        if (trimmed.Length < 4)
            return (true, "", "");

        var model = ResolveHintModel(settings);
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = WriterAssistantPrompts.NextWordContinuation },
                new { role = "user", content = "Full draft so far (read the whole line):\n" + trimmed }
            },
            temperature = 0.1,
            max_tokens = 24
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, cancellationToken);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        if (!res.IsSuccessStatusCode)
            return (false, "", $"HTTP {(int)res.StatusCode}: {Truncate(body, 200)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var raw = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim() ?? "";
            raw = NormalizeHintResponse(raw);
            if (raw.Length == 0
                || string.Equals(raw, "NONE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "NONE.", StringComparison.OrdinalIgnoreCase))
                return (true, "", "");
            if (trimmed.EndsWith(raw, StringComparison.OrdinalIgnoreCase))
                return (true, "", "");
            return (true, raw, "");
        }
        catch
        {
            return (false, "", "Unexpected hint response.");
        }
    }

    private static string NormalizeHintResponse(string s)
    {
        s = s.Trim();
        if (s.Length >= 2
            && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
