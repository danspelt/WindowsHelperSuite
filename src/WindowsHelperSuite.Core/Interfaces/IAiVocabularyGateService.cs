namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>
/// Asks an LLM whether a new token is worth persisting to the personal word bank (likely to be typed again).
/// </summary>
public interface IAiVocabularyGateService : IDisposable
{
    /// <param name="item">Normalized word or phrase (user typed).</param>
    /// <param name="isPhrase">True when <paramref name="item"/> contains spaces.</param>
    /// <param name="recentContext">Optional short prefix of what they were writing (not secrets).</param>
    Task<bool> ShouldRememberNewItemAsync(string item, bool isPhrase, string? recentContext, CancellationToken cancellationToken = default);
}
