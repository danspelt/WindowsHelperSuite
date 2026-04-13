using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IOverlayService
{
    void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions);
    void HideSuggestions();
    /// <param name="contextSummary">Short hint (e.g. completing "x" after "y").</param>
    /// <param name="fullSentenceWords">All words in the current sentence from the writer buffer (optional).</param>
    void SetContextMode(string? contextSummary, string? fullSentenceWords = null);

    /// <summary>Small non-blocking footer hint (e.g. AI status). Null clears.</summary>
    void SetOverlayStatusHint(string? message);
    void MoveToNextPage();
    void MoveToPreviousPage();
    /// <summary>Moves keyboard highlight among on-page suggestions (-1 = up/previous, +1 = down/next).</summary>
    void MoveSuggestionHighlight(int delta);

    /// <summary>Slot (1–9) of the keyboard-highlighted suggestion on the current page, if any.</summary>
    int? GetHighlightedSuggestionSlot();
    event EventHandler<int>? SuggestionSelected;
    /// <summary>Fired when keyboard highlight moves to a suggestion; string is display text.</summary>
    event EventHandler<string?>? SuggestionHighlightChanged;
}
