using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IOverlayService
{
    void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions);
    void HideSuggestions();
    /// <param name="contextSummary">Short hint (e.g. completing "x" after "y").</param>
    /// <param name="fullSentenceWords">All words in the current sentence from the writer buffer (optional).</param>
    void SetContextMode(string? contextSummary, string? fullSentenceWords = null);
    void MoveToNextPage();
    void MoveToPreviousPage();
    event EventHandler<int>? SuggestionSelected;
}
