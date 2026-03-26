namespace WindowsHelperSuite.Core.Models;

/// <summary>
/// Fired when a word is completed (space, punctuation, line break). Used for sentence-aware fixes.
/// </summary>
public sealed class WordTypedEventArgs : EventArgs
{
    public required string Word { get; init; }

    /// <summary>Sentence buffer text strictly before the completed word.</summary>
    public required string TextBeforeWord { get; init; }
}
