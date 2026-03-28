namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Manages personal context memory for smarter AI suggestions.
/// Stores names, repeated phrases, preferred sentence starters, custom vocabulary.
/// </summary>
public interface IAiContextService
{
    /// <summary>
    /// Records a phrase that the user has typed or selected.
    /// </summary>
    void RecordPhrase(string phrase);

    /// <summary>
    /// Gets frequently used phrases for the current context.
    /// </summary>
    IReadOnlyList<string> GetFrequentPhrases(string? context = null, int count = 5);

    /// <summary>
    /// Records a name that the user has typed.
    /// </summary>
    void RecordName(string name);

    /// <summary>
    /// Gets known names for suggestion prioritization.
    /// </summary>
    IReadOnlyList<string> GetKnownNames();

    /// <summary>
    /// Clears all learned context.
    /// </summary>
    void ClearContext();
}
