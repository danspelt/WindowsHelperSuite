using System.Text;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// AI rewrite service for text transformation.
/// </summary>
public class AiRewriteService : IAiRewriteService
{
    private readonly ILoggingService _loggingService;
    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;

    public AiRewriteService(ILoggingService loggingService, AiSettings settings)
    {
        _loggingService = loggingService;
        _settings = settings;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<string> RewriteAsync(AiRewriteRequest request, CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableAiRewriteTools)
        {
            return request.Text;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await RewriteWithAiAsync(request, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("AI rewrite request timed out");
            return request.Text;
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"AI rewrite failed: {ex.Message}");
            return request.Text; // Return original on failure
        }
    }

    public async Task<string> FixGrammarAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new AiRewriteRequest
        {
            Text = text,
            Tone = RewriteTone.FixGrammar
        };
        return await RewriteAsync(request, cancellationToken);
    }

    private async Task<string> RewriteWithAiAsync(AiRewriteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _loggingService.Debug("No AI API key configured, skipping rewrite");
            return request.Text;
        }

        // Placeholder for AI integration
        await Task.CompletedTask;
        return request.Text;
    }

    private string BuildRewritePrompt(AiRewriteRequest request)
    {
        var toneInstruction = request.Tone switch
        {
            RewriteTone.Clearer => "Make this clearer and easier to understand",
            RewriteTone.Shorter => "Make this shorter and more concise",
            RewriteTone.MoreFormal => "Make this more formal and professional",
            RewriteTone.Friendlier => "Make this friendlier and more approachable",
            RewriteTone.FixGrammar => "Fix any grammar and spelling errors",
            _ => "Improve this text"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"You are a writing assistant. {toneInstruction}.");
        sb.AppendLine("Return only the rewritten text, no explanations.");
        sb.AppendLine();
        sb.AppendLine($"Text to rewrite: \"{request.Text}\"");
        sb.AppendLine();
        sb.AppendLine("Rewritten text:");
        return sb.ToString();
    }
}
