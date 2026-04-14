using System.Net.Http.Json;
using System.Text.Json;

namespace WindowsHelperSuite.Writer.Llm;

public sealed class LocalLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly LocalLlmOptions _options;

    public LocalLlmClient(HttpClient httpClient, LocalLlmOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string?> GetRawCompletionAsync(string prompt, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.TimeoutMs);

        var payload = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            max_tokens = 40
        };

        var url = BuildChatCompletionsUrl(_options.BaseUrl);
        var response = await _httpClient.PostAsJsonAsync(url, payload, cts.Token).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    /// <summary>Resolves <c>…/v1/chat/completions</c> whether <see cref="LocalLlmOptions.BaseUrl"/> is host-only or already ends with <c>/v1</c>.</summary>
    internal static string BuildChatCompletionsUrl(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        if (root.Length == 0)
        {
            root = "http://localhost:1234/v1";
        }

        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{root}/chat/completions";
        }

        return $"{root}/v1/chat/completions";
    }
}
