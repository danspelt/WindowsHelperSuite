using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Overlay.Views;
using System.Windows;

namespace WindowsHelperSuite.Overlay.Services;

public class OverlayService : IOverlayService, IDisposable
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private OverlayWindow? _overlayWindow;
    private List<SuggestionItem> _allSuggestions = [];
    private List<SuggestionItem> _currentPageSuggestions = [];
    private int _currentPage = 0;
    private int _totalPages = 1;
    private const int ItemsPerPage = 9;
    private OverlayLayout _layout = OverlayLayout.Vertical;
    private int _lastLogPageSuggestionCount = int.MinValue;
    private int _lastLogSuggestionPageIndex = int.MinValue;
    private int _lastLogCaretX = int.MinValue;
    private int _lastLogCaretY = int.MinValue;

    public event EventHandler<int>? SuggestionSelected;

    public OverlayService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;

        _layout = _settingsService.Settings.Ui.Layout;
    }

    public void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions)
    {
        _allSuggestions = suggestions.ToList();
        _currentPage = 0;
        CalculatePages();
        ShowCurrentPage();

        // Position at actual caret location
        PositionAtCaret();
    }

    public void HideSuggestions()
    {
        RunOnUiThread(() =>
        {
            _overlayWindow?.HideSuggestions();
            // Reset layout lock when hiding so next show can re-detect
            _overlayWindow?.ResetLayoutLock();
        });
        _loggingService.Debug("Overlay hidden, layout lock reset");
    }

    public void MoveToNextPage()
    {
        if (_currentPage < _totalPages - 1)
        {
            _currentPage++;
            ShowCurrentPage();
            PositionAtCaret();
            _loggingService.Information($"Moved to page {_currentPage + 1} of {_totalPages}");
        }
    }

    public void MoveToPreviousPage()
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            ShowCurrentPage();
            PositionAtCaret();
            _loggingService.Information($"Moved to page {_currentPage + 1} of {_totalPages}");
        }
    }

    public void HandleSelectionKey(int slot)
    {
        _loggingService.Debug($"HandleSelectionKey called with slot {slot}, _currentPageSuggestions count: {_currentPageSuggestions.Count}");
        var suggestion = _currentPageSuggestions.FirstOrDefault(s => s.Slot == slot);
        if (suggestion != null)
        {
            _loggingService.Debug($"Found suggestion: {suggestion.DisplayText}, raising SuggestionSelected event");
            SuggestionSelected?.Invoke(this, slot);
            _loggingService.Information($"Suggestion selected: slot {slot} - {suggestion.DisplayText}");
        }
        else
        {
            _loggingService.Warning($"No suggestion found for slot {slot}. Available slots: {string.Join(",", _currentPageSuggestions.Select(s => s.Slot))}");
        }
    }

    public void FlashSelection(int slot)
    {
        RunOnUiThread(() => _overlayWindow?.FlashSelection(slot));
    }

    public void SetLayout(OverlayLayout layout)
    {
        _layout = layout;
        RunOnUiThread(() => _overlayWindow?.SetLayout(layout));
        _settingsService.Settings.Ui.Layout = layout;
        _settingsService.Save();
        _loggingService.Information($"Overlay layout set to: {layout}");
    }

    public OverlayLayout GetCurrentLayout() => _layout;

    public void SetContextMode(string? contextSummary, string? fullSentenceWords = null)
    {
        RunOnUiThread(() => _overlayWindow?.SetContextMode(contextSummary, fullSentenceWords));
    }

    public void ShowSpeakerIndicator(string spokenText)
    {
        RunOnUiThread(() => _overlayWindow?.ShowSpeakerIndicator(spokenText));
    }

    public void PositionNearCaret(int x, int y)
    {
        // Fallback method - use provided coordinates
        EnsureWindowCreated();
        RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, ScreenPosition.Above));
    }

    private void PositionAtCaret()
    {
        EnsureWindowCreated();

        // Apply current layout setting
        RunOnUiThread(() => _overlayWindow?.SetLayout(_layout));

        // Try to get actual caret position
        if (Win32Caret.GetCaretPosition(out var x, out var y))
        {
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, ScreenPosition.Above));
            if (x != _lastLogCaretX || y != _lastLogCaretY)
            {
                _lastLogCaretX = x;
                _lastLogCaretY = y;
                _loggingService.Debug($"Overlay positioned at caret: {x}, {y}");
            }
        }
        else
        {
            // Fallback to screen center
            var screen = System.Windows.SystemParameters.WorkArea;
            var centerX = (int)(screen.Left + screen.Width / 2);
            var centerY = (int)(screen.Top + screen.Height / 2);
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(centerX, centerY, ScreenPosition.Above));
            if (centerX != _lastLogCaretX || centerY != _lastLogCaretY)
            {
                _lastLogCaretX = centerX;
                _lastLogCaretY = centerY;
                _loggingService.Debug($"Overlay positioned at screen center (caret unavailable): {centerX}, {centerY}");
            }
        }
    }

    private void CalculatePages()
    {
        _totalPages = (int)Math.Ceiling(_allSuggestions.Count / (double)ItemsPerPage);
        if (_totalPages < 1) _totalPages = 1;
    }

    private void ShowCurrentPage()
    {
        EnsureWindowCreated();

        var startIndex = _currentPage * ItemsPerPage;
        var count = Math.Min(ItemsPerPage, _allSuggestions.Count - startIndex);

        _currentPageSuggestions = _allSuggestions
            .Skip(startIndex)
            .Take(count)
            .Select((s, i) => new SuggestionItem
            {
                Slot = i + 1,
                DisplayText = s.DisplayText,
                InsertText = s.InsertText,
                Kind = s.Kind,
                Score = s.Score
            })
            .ToList();

        RunOnUiThread(() =>
        {
            if (_overlayWindow == null)
            {
                return;
            }

            var ui = _settingsService.Settings.Ui;
            _overlayWindow.SetLayout(_layout);
            _overlayWindow.ApplyUiSettings(ui.FontSize, ui.Opacity, ui.LargeTextMode,
                ui.AccentColor, ui.OverlayBackgroundColor, ui.CardColor, ui.TextColor);
            _overlayWindow.ShowSuggestions(_currentPageSuggestions, _currentPage, _totalPages);
        });
        if (_currentPageSuggestions.Count != _lastLogPageSuggestionCount
            || _currentPage != _lastLogSuggestionPageIndex)
        {
            _lastLogPageSuggestionCount = _currentPageSuggestions.Count;
            _lastLogSuggestionPageIndex = _currentPage;
            _loggingService.Debug($"Showing {_currentPageSuggestions.Count} suggestions (page {_currentPage + 1})");
        }
    }

    private void EnsureWindowCreated()
    {
        if (_overlayWindow == null)
        {
            RunOnUiThread(() =>
            {
                if (_overlayWindow != null)
                {
                    return;
                }

                _overlayWindow = new OverlayWindow();
                var ui = _settingsService.Settings.Ui;
                _overlayWindow.SetLayout(_layout);
                _overlayWindow.ApplyUiSettings(ui.FontSize, ui.Opacity, ui.LargeTextMode,
                    ui.AccentColor, ui.OverlayBackgroundColor, ui.CardColor, ui.TextColor);
                _overlayWindow.SuggestionSelected += OnSuggestionSelected;
                _overlayWindow.NextPageRequested += (s, e) => MoveToNextPage();
                _overlayWindow.PreviousPageRequested += (s, e) => MoveToPreviousPage();
            });
        }
    }

    /// <summary>
    /// Never runs <paramref name="action"/> on a random thread when the WPF dispatcher is unavailable —
    /// that was creating <see cref="OverlayWindow"/> off the UI thread and crashing the process.
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _loggingService.Warning("Overlay UI skipped: Application.Current is not available yet.");
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Overlay UI error: {ex.Message}");
        }
    }

    private void OnSuggestionSelected(object? sender, int slot)
    {
        SuggestionSelected?.Invoke(this, slot);
    }

    public void Dispose()
    {
        RunOnUiThread(() =>
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
        });
    }
}
