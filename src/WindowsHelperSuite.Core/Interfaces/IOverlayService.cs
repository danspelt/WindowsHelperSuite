using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IOverlayService
{
    void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions);
    void HideSuggestions();

    /// <summary>True when the cursor is over the visible overlay window (screen coordinates).</summary>
    bool IsCursorOverOverlay();

    /// <summary>Moves the overlay near the current caret without refreshing suggestion content.</summary>
    void RepositionAtCaret();
    /// <param name="contextSummary">Short hint (e.g. completing "x" after "y").</param>
    /// <param name="fullSentenceWords">All words in the current sentence from the writer buffer (optional).</param>
    void SetContextMode(string? contextSummary, string? fullSentenceWords = null);

    /// <summary>Small non-blocking footer hint (e.g. AI status). Null clears.</summary>
    void SetOverlayStatusHint(string? message);
    void MoveToNextPage();
    void MoveToPreviousPage();
    /// <summary>Toggle overlay between horizontal and vertical suggestion layout.</summary>
    void ToggleHorizontalVerticalLayout();
    /// <summary>Moves keyboard highlight among on-page suggestions (-1 = up/previous, +1 = down/next).</summary>
    void MoveSuggestionHighlight(int delta);

    /// <summary>Slot (1–9) of the keyboard-highlighted suggestion on the current page, if any.</summary>
    int? GetHighlightedSuggestionSlot();
    event EventHandler<int>? SuggestionSelected;
    /// <summary>Fired when keyboard highlight moves to a suggestion; string is display text.</summary>
    event EventHandler<string?>? SuggestionHighlightChanged;
    /// <summary>Fired when the user clicks the overlay's close (✕) button. Consumers should put the writer to sleep.</summary>
    event EventHandler? CloseRequested;

    /// <summary>Temporarily suppress the writer (no overlay shown) for the given duration.</summary>
    void SuppressFor(TimeSpan duration);
    /// <summary>Clear any active suppression so the writer can appear again.</summary>
    void ClearSuppression();
    /// <summary>True if the writer is currently suppressed.</summary>
    bool IsSuppressed { get; }
    /// <summary>UTC timestamp until which the writer is suppressed (null = not suppressed).</summary>
    DateTime? SuppressedUntilUtc { get; }
}
