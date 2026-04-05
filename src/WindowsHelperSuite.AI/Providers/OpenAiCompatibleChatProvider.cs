using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Providers;

public class OpenAiCompatibleChatProvider : IChatProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _log;
    private readonly ChatOptions _options;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => _options.ProviderName;

    public OpenAiCompatibleChatProvider(ChatOptions options, ILoggingService log)
    {
        _options = options;
        _log = log;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        var host = SafeHost(_options.BaseUrl);
        _log.Information($"[Chat] SendAsync → host={host}, model={request.Model}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var body = BuildRequestBody(request, stream: false);
            using var httpReq = CreateHttpRequest(body);
            using var httpRes = await _httpClient.SendAsync(httpReq, ct);

            var json = await httpRes.Content.ReadAsStringAsync(ct);

            if (!httpRes.IsSuccessStatusCode)
            {
                _log.Warning($"[Chat] API error {httpRes.StatusCode}: {Truncate(json, 300)}");
                return new ChatResponse
                {
                    Success = false,
                    ErrorMessage = ToUserFriendlyHttpError((int)httpRes.StatusCode, json),
                    Model = request.Model,
                };
            }

            var content = ExtractContentFromResponse(json);
            sw.Stop();
            _log.Information($"[Chat] SendAsync complete in {sw.ElapsedMilliseconds}ms, {content.Length} chars");
            return new ChatResponse
            {
                Content = content,
                Model = request.Model,
                Success = true,
            };
        }
        catch (OperationCanceledException)
        {
            _log.Information("[Chat] Request cancelled");
            return new ChatResponse { Success = false, ErrorMessage = "Cancelled.", Model = request.Model };
        }
        catch (Exception ex)
        {
            _log.Error($"[Chat] SendAsync failed: {ex.Message}", ex);
            return new ChatResponse { Success = false, ErrorMessage = ToUserFriendlyError(ex), Model = request.Model };
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var host = SafeHost(_options.BaseUrl);
        _log.Information($"[Chat] StreamAsync → host={host}, model={request.Model}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage? httpRes = null;
        try
        {
            var body = BuildRequestBody(request, stream: true);
            using var httpReq = CreateHttpRequest(body);
            httpRes = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!httpRes.IsSuccessStatusCode)
            {
                var errorJson = await httpRes.Content.ReadAsStringAsync(ct);
                _log.Warning($"[Chat] Stream API error {httpRes.StatusCode}: {Truncate(errorJson, 300)}");
                throw new InvalidOperationException(
                    ToUserFriendlyHttpError((int)httpRes.StatusCode, errorJson));
            }

            using var stream = await httpRes.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (!line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]")
                {
                    yield return new ChatStreamChunk { IsCompleted = true };
                    yield break;
                }

                var delta = ExtractDeltaFromStreamChunk(data);
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return new ChatStreamChunk { TextDelta = delta };
                }
            }

            yield return new ChatStreamChunk { IsCompleted = true };
        }
        finally
        {
            httpRes?.Dispose();
            sw.Stop();
            _log.Information($"[Chat] StreamAsync finished in {sw.ElapsedMilliseconds}ms");
        }
    }

    private HttpRequestMessage CreateHttpRequest(string jsonBody)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        return req;
    }

    private string BuildRequestBody(ChatRequest request, bool stream)
    {
        var messages = new List<object>();

        var systemPrompt = !string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? request.SystemPrompt
            : _options.DefaultSystemPrompt;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        foreach (var m in request.Messages)
        {
            messages.Add(new { role = m.Role, content = m.Content });
        }

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model,
            messages,
            temperature = request.Temperature,
            stream,
        };

        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private static string ExtractContentFromResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static string ExtractDeltaFromStreamChunk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return "";
            var delta = choices[0].GetProperty("delta");
            return delta.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ToUserFriendlyError(Exception ex) => ex switch
    {
        TaskCanceledException => "The AI request timed out or was cancelled.",
        HttpRequestException => "Could not reach the AI service. Check the base URL.",
        InvalidOperationException ioe => ioe.Message,
        _ => $"Unexpected AI error: {ex.Message}",
    };

    private static string ToUserFriendlyHttpError(int statusCode, string rawBody) => statusCode switch
    {
        401 => "Authentication failed. Check your API key.",
        403 => "Access denied. Your API key may lack permissions.",
        404 => "Endpoint not found. Check the base URL and model name.",
        429 => "Rate limited. Please wait a moment and try again.",
        >= 500 => $"The AI server returned an error ({statusCode}). Try again later.",
        _ => $"AI request failed ({statusCode}): {Truncate(rawBody, 150)}",
    };

    private static string SafeHost(string url)
    {
        try { return new Uri(url).Host; }
        catch { return "(invalid-url)"; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
