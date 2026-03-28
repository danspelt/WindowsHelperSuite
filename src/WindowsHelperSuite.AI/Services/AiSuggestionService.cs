using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// AI suggestion service that provides phrase completions.
/// Implements timeout and fallback to ensure typing is never blocked.
/// </summary>
public class AiSuggestionService : IAiSuggestionService
{
    private readonly ILoggingService _loggingService;
    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;

    public AiSuggestionService(ILoggingService loggingService, AiSettings settings)
    {
        _loggingService = loggingService;
        _settings = settings;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(settings.AiTimeoutMs) };
    }

    public async Task<IReadOnlyList<AiSuggestionResult>> GetPhraseSuggestionsAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableAiSuggestions || !_settings.EnableAiPhraseCompletion)
        {
            return Array.Empty<AiSuggestionResult>();
        }

        // Create a timeout token source
        using var timeoutCts = new CancellationTokenSource(_settings.AiTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var suggestions = await GetSuggestionsFromAiAsync(request, linkedCts.Token);
            return suggestions.Take(_settings.MaxAiSuggestions).ToList();
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("AI suggestion request timed out or was cancelled");
            return Array.Empty<AiSuggestionResult>();
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"AI suggestion failed: {ex.Message}");
            return Array.Empty<AiSuggestionResult>();
        }
    }

    public async Task<IReadOnlyList<string>> GetQuickCompletionsAsync(
        string currentText,
        CancellationToken cancellationToken = default)
    {
        // Quick completions use a simpler, faster approach
        // This is a placeholder implementation - integrate with your preferred AI provider
        await Task.Delay(1, cancellationToken); // Minimal async work
        return Array.Empty<string>();
    }

    private async Task<IReadOnlyList<AiSuggestionResult>> GetSuggestionsFromAiAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        // Placeholder for AI integration
        // In production, this would call OpenAI, Azure, or local LLM
        // For now, return empty to ensure the app works without AI configured

        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _loggingService.Debug("No AI API key configured, skipping AI suggestions");
            return Array.Empty<AiSuggestionResult>();
        }

        // Example OpenAI integration structure:
        // var prompt = BuildPrompt(request);
        // var response = await CallOpenAiAsync(prompt, cancellationToken);
        // return ParseResponse(response);

        await Task.CompletedTask;
        return Array.Empty<AiSuggestionResult>();
    }

    private string BuildPrompt(AiSuggestionRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a writing assistant. Given the current text, suggest the next few words or phrases that would naturally follow.");
        sb.AppendLine("Return only the suggestions, one per line, no numbering, no explanations.");
        sb.AppendLine();
        sb.AppendLine($"Current text: \"{request.CurrentText}\"");
        if (!string.IsNullOrEmpty(request.PreviousSentence))
        {
            sb.AppendLine($"Previous context: \"{request.PreviousSentence}\"");
        }
        sb.AppendLine();
        sb.AppendLine($"Suggest {request.MaxSuggestions} natural continuations:");
        return sb.ToString();
    }
}
