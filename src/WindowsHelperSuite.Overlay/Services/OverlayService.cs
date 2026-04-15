using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Overlay.Views;
using WindowsHelperSuite.Infrastructure.Hooks;
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
    public event EventHandler<string?>? SuggestionHighlightChanged;

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
            _overlayWindow?.SetOverlayStatusHint(null);
            _overlayWindow?.HideSuggestions();
            // Reset layout lock when hiding so next show can re-detect
            _overlayWindow?.ResetLayoutLock();
        });
        _loggingService.Debug("Overlay hidden, layout lock reset");
    }

    public void SetOverlayStatusHint(string? message)
    {
        RunOnUiThread(() => _overlayWindow?.SetOverlayStatusHint(message));
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

    public void MoveSuggestionHighlight(int delta)
    {
        if (delta is not (-1) and not 1)
        {
            return;
        }

        RunOnUiThread(() => _overlayWindow?.MoveSuggestionHighlight(delta));
    }

    public int? GetHighlightedSuggestionSlot()
    {
        EnsureWindowCreated();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || _overlayWindow == null)
        {
            return null;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                return _overlayWindow.GetHighlightedSuggestionSlot();
            }

            return dispatcher.Invoke(() => _overlayWindow.GetHighlightedSuggestionSlot());
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"GetHighlightedSuggestionSlot: {ex.Message}");
            return null;
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
        EnsureWindowCreated();
        var pos = MapCaretPlacement();
        RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, pos));
    }

    private ScreenPosition MapCaretPlacement() =>
        _settingsService.Settings.Ui.OverlayCaretPlacement switch
        {
            WriterOverlayCaretPlacement.Below => ScreenPosition.Below,
            WriterOverlayCaretPlacement.Above => ScreenPosition.Above,
            _ => ScreenPosition.Auto,
        };

    /// <summary>Reference point for auto layout (horizontal vs vertical) and fallbacks.</summary>
    private static void TryGetLayoutReferencePoint(out int x, out int y)
    {
        if (Win32Caret.GetCaretPosition(out x, out y))
        {
            return;
        }

        var screen = System.Windows.SystemParameters.WorkArea;
        x = (int)(screen.Left + screen.Width / 2);
        y = (int)(screen.Top + screen.Height / 2);
    }

    private void PositionAtCaret()
    {
        EnsureWindowCreated();

        var pos = MapCaretPlacement();
        RunOnUiThread(() => _overlayWindow?.SetLayout(_layout));

        // Try to get text field bounds for overlap avoidance
        System.Windows.Rect? textFieldBounds = null;
        if (Win32Caret.TryGetTextInputBounds(out var bounds) && !bounds.IsEmpty)
        {
            textFieldBounds = bounds;
        }

        if (Win32Caret.GetCaretPosition(out var x, out var y))
        {
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, pos, textFieldBounds));
            if (x != _lastLogCaretX || y != _lastLogCaretY)
            {
                _lastLogCaretX = x;
                _lastLogCaretY = y;
                _loggingService.Debug($"Overlay positioned at caret: {x}, {y}");
            }
        }
        else
        {
            var screen = System.Windows.SystemParameters.WorkArea;
            var centerX = (int)(screen.Left + screen.Width / 2);
            var centerY = (int)(screen.Top + screen.Height / 2);
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(centerX, centerY, pos, textFieldBounds));
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

        TryGetLayoutReferencePoint(out var layoutX, out var layoutY);
        RunOnUiThread(() =>
        {
            if (_overlayWindow == null)
            {
                return;
            }

            var ui = _settingsService.Settings.Ui;
            _overlayWindow.SetLayout(_layout);
            _overlayWindow.ApplyUiSettings(ui.FontSize, ui.Opacity, ui.LargeTextMode,
                ui.AccentColor, ui.OverlayBackgroundColor, ui.CardColor, ui.TextColor,
                ui.FontFamily, ui.FontWeight, ui.OverlayFadeTransitionMs);
            _overlayWindow.ShowSuggestions(_currentPageSuggestions, _currentPage, _totalPages,
                layoutX, layoutY, ui.OverlayFadeTransitionMs);
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
                    ui.AccentColor, ui.OverlayBackgroundColor, ui.CardColor, ui.TextColor,
                    ui.FontFamily, ui.FontWeight, ui.OverlayFadeTransitionMs);
                _overlayWindow.SuggestionSelected += OnSuggestionSelected;
                _overlayWindow.SuggestionHighlightChanged += OnSuggestionHighlightChanged;
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

    private void OnSuggestionHighlightChanged(object? sender, string? displayText)
    {
        SuggestionHighlightChanged?.Invoke(this, displayText);
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
