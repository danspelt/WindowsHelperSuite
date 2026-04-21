using System.Text.RegularExpressions;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Llm;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class LocalLlmProvider : IPredictionProvider
{
    private static readonly Regex HorizontalWhitespaceRun = new("[ \\t\\u00A0]{2,}", RegexOptions.Compiled);

    private readonly LocalLlmClient _client;
    private readonly LocalLlmOptions _options;

    public LocalLlmProvider(LocalLlmClient client, LocalLlmOptions options)
    {
        _client = client;
        _options = options;
    }

    public string Name => "local-llm";

    public async Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Context.TypingMode == WriterTypingMode.Code)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(request.CurrentSentence) &&
            string.IsNullOrWhiteSpace(request.CurrentToken))
        {
            return [];
        }

        var prompt = BuildPrompt(request);

        try
        {
            var raw = await _client.GetRawCompletionAsync(prompt, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            var max = Math.Clamp(_options.MaxSuggestions, 1, 8);
            return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => HorizontalWhitespaceRun.Replace(x.Trim().TrimStart('-', '•', ' ', '"'), " "))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Select(x => new PredictionCandidate
                {
                    Text = x,
                    Source = Name,
                    BaseScore = 0.7,
                    IsPhrase = x.Contains(' ', StringComparison.Ordinal)
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string BuildPrompt(PredictionRequest request)
    {
        var modeHint = request.Context.TypingMode switch
        {
            WriterTypingMode.Chat =>
                "Prefer short, conversational completions (messages, chat).",
            WriterTypingMode.Email =>
                "Prefer polite, complete-sentence phrasing suitable for email.",
            WriterTypingMode.Code =>
                "Prefer identifiers and short tokens (no prose).",
            _ => "Balanced general writing suggestions."
        };

        var prev = request.PreviousCompletedWord?.Trim() ?? "";
        var prevLine = string.IsNullOrWhiteSpace(prev)
            ? ""
            : $"Word before the fragment being typed: \"{prev}\"\n";

        return $"""
            Complete the user's text with up to 4 short writing suggestions.

            {modeHint}
            Context mode: {request.Context.TypingMode}
            {prevLine}Text so far: "{request.CurrentSentence}"

            Return one suggestion per line.
            Do not number them.
            Keep suggestions short.
            """;
    }
}
