using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IOverlayService
{
    void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions);
    void HideSuggestions();
    void SetContextMode(string? contextText);
    void MoveToNextPage();
    void MoveToPreviousPage();
    event EventHandler<int>? SuggestionSelected;
}
