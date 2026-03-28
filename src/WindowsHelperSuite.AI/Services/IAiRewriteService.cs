using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Provides AI-powered text rewriting capabilities.
/// </summary>
public interface IAiRewriteService
{
    /// <summary>
    /// Rewrites the given text according to the specified tone/style.
    /// </summary>
    Task<string> RewriteAsync(
        AiRewriteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fixes grammar and punctuation in the given text.
    /// </summary>
    Task<string> FixGrammarAsync(
        string text,
        CancellationToken cancellationToken = default);
}
