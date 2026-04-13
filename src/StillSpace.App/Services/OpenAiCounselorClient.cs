using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StillSpace.Counseling;

namespace StillSpace.Services;

public sealed class OpenAiCounselorClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public void Dispose() => _http.Dispose();

    public string? ResolveApiKey(StillSpaceSettings settings)
    {
        var k = settings.OpenAiApiKey?.Trim();
        if (!string.IsNullOrEmpty(k)) return k;
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    }

    public string ResolveChatModel(StillSpaceSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiChatModel)
            ? settings.OpenAiChatModel.Trim()
            : (Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL")?.Trim() ?? "gpt-4o");

    public string ResolveTtsModel(StillSpaceSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiTtsModel)
            ? settings.OpenAiTtsModel.Trim()
            : (Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL")?.Trim() ?? "tts-1");

    public string ResolveTtsVoice(StillSpaceSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiTtsVoice)
            ? settings.OpenAiTtsVoice.Trim()
            : (Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE")?.Trim() ?? "alloy");

    public async Task<(bool Ok, string Text, string Error)> CompleteAsync(
        StillSpaceSettings settings,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var key = ResolveApiKey(settings);
        if (string.IsNullOrEmpty(key))
        {
            var envHint = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StillSpace",
                ".env");
            return (false, "",
                $"Missing OpenAI API key. Paste it in Settings, set the OPENAI_API_KEY user environment variable, or create a file at: {envHint}");
        }

        var model = ResolveChatModel(settings);
        var payload = new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            temperature = 0.65,
            max_tokens = 900
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
            return (false, "", $"HTTP {(int)res.StatusCode}: {Truncate(body, 400)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim() ?? "";
            return (true, text, "");
        }
        catch
        {
            return (false, "", "Unexpected response from chat completions.");
        }
    }

    public async Task<(bool Ok, byte[]? Audio, string Error)> TextToSpeechAsync(
        StillSpaceSettings settings,
        string text,
        CancellationToken cancellationToken = default)
    {
        var key = ResolveApiKey(settings);
        if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(text))
            return (false, null, "no_key_or_text");

        var model = ResolveTtsModel(settings);
        var voice = ResolveTtsVoice(settings);
        var payload = new
        {
            model,
            voice,
            input = text.Length > 4000 ? text[..4000] : text
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
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
            return (false, null, ex.Message);
        }

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(cancellationToken);
            return (false, null, $"HTTP {(int)res.StatusCode}: {Truncate(err, 400)}");
        }

        var bytes = await res.Content.ReadAsByteArrayAsync(cancellationToken);
        return (true, bytes, "");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
