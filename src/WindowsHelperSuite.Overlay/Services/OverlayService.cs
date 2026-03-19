#pragma warning disable CS0067

using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Overlay.Services;

public class OverlayService : IOverlayService
{
    public event EventHandler<int>? SuggestionSelected;

    public void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions) { }
    public void HideSuggestions() { }
    public void MoveToNextPage() { }
    public void MoveToPreviousPage() { }
}

#pragma warning restore CS0067
