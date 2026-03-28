using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Provides AI-powered phrase suggestions for the Writer module.
/// Never blocks typing - suggestions load asynchronously.
/// </summary>
public interface IAiSuggestionService
{
    /// <summary>
    /// Gets AI-powered phrase suggestions based on current text context.
    /// This method is async and will timeout if AI is slow/unavailable.
    /// </summary>
    Task<IReadOnlyList<AiSuggestionResult>> GetPhraseSuggestionsAsync(
        AiSuggestionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quick next-word predictions using a lighter model/fallback.
    /// </summary>
    Task<IReadOnlyList<string>> GetQuickCompletionsAsync(
        string currentText,
        CancellationToken cancellationToken = default);
}
