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
    public event EventHandler? CloseRequested;

    // ── Writer suppression (close-button disables writer for a period) ──
    private DateTime? _suppressedUntilUtc;
    public bool IsSuppressed => _suppressedUntilUtc.HasValue && DateTime.UtcNow < _suppressedUntilUtc.Value;
    public DateTime? SuppressedUntilUtc => _suppressedUntilUtc;

    public void SuppressFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            ClearSuppression();
            return;
        }
        _suppressedUntilUtc = DateTime.UtcNow + duration;
        _loggingService.Information($"Writer suppressed until {_suppressedUntilUtc:HH:mm:ss} UTC (for {duration.TotalMinutes:F0} min)");
        HideSuggestions();
    }

    public void ClearSuppression()
    {
        if (_suppressedUntilUtc.HasValue)
        {
            _loggingService.Information("Writer suppression cleared");
        }
        _suppressedUntilUtc = null;
    }

    public OverlayService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;

        _layout = _settingsService.Settings.Ui.Layout;
    }

    public void ShowSuggestions(IReadOnlyList<SuggestionItem> suggestions)
    {
        // If the writer is suppressed (user closed it via X button), don't show anything.
        if (IsSuppressed)
        {
            return;
        }

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
        Rect? uiaBounds = null;
        if (Win32Caret.TryGetTextInputBounds(out var b) && !b.IsEmpty)
        {
            uiaBounds = b;
        }

        var exclusion = BuildTextExclusionRect(x, y, uiaBounds);
        RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, pos, exclusion));
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

        Rect? uiaBounds = null;
        if (Win32Caret.TryGetTextInputBounds(out var bounds) && !bounds.IsEmpty)
        {
            uiaBounds = bounds;
        }

        if (Win32Caret.GetCaretPosition(out var x, out var y))
        {
            var exclusion = BuildTextExclusionRect(x, y, uiaBounds);
            
            // Check if we should position on next screen
            var screenPreference = _settingsService.Settings.Ui.OverlayScreenPreference;
            if (screenPreference == WriterOverlayScreenPreference.NextScreen)
            {
                // Position on next screen instead of current
                var nextScreenCenter = GetNextScreenCenter(x, y);
                if (nextScreenCenter.HasValue)
                {
                    var nextExclusion = BuildTextExclusionRect(nextScreenCenter.Value.X, nextScreenCenter.Value.Y, uiaBounds);
                    RunOnUiThread(() => _overlayWindow?.PositionNearPoint(nextScreenCenter.Value.X, nextScreenCenter.Value.Y, pos, nextExclusion));
                    if (nextScreenCenter.Value.X != _lastLogCaretX || nextScreenCenter.Value.Y != _lastLogCaretY)
                    {
                        _lastLogCaretX = nextScreenCenter.Value.X;
                        _lastLogCaretY = nextScreenCenter.Value.Y;
                        _loggingService.Debug($"Overlay positioned on next screen: {nextScreenCenter.Value.X}, {nextScreenCenter.Value.Y}");
                    }
                    return;
                }
            }
            
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(x, y, pos, exclusion));
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
            var exclusion = BuildTextExclusionRect(centerX, centerY, uiaBounds);
            RunOnUiThread(() => _overlayWindow?.PositionNearPoint(centerX, centerY, pos, exclusion));
            if (centerX != _lastLogCaretX || centerY != _lastLogCaretY)
            {
                _lastLogCaretX = centerX;
                _lastLogCaretY = centerY;
                _loggingService.Debug($"Overlay positioned at screen center (caret unavailable): {centerX}, {centerY}");
            }
        }
    }

    private (int X, int Y)? GetNextScreenCenter(int currentX, int currentY)
    {
        if (Win32Screen.TryGetNextScreenWorkArea(currentX, currentY, out var left, out var top, out var right, out var bottom))
        {
            var centerX = left + (right - left) / 2;
            var centerY = top + (bottom - top) / 2;
            return (centerX, centerY);
        }
        return null;
    }

    /// <summary>
    /// Screen-space rectangle the overlay must not cover: UIA text field when available, else Win32 caret rect
    /// inflated generously so Electron/multiline editors still get a safe zone.
    /// </summary>
    private static Rect BuildTextExclusionRect(int caretX, int caretY, Rect? uiaBounds)
    {
        Rect caretBase;
        if (Win32Caret.TryGetCaretScreenRect(out var caretRc) && !caretRc.IsEmpty)
        {
            caretBase = caretRc;
        }
        else
        {
            caretBase = new Rect(caretX - 100, caretY - 28, 200, 32);
        }

        if (uiaBounds is { IsEmpty: false } u)
        {
            var uiaInflated = ExpandRectMargins(u, 6, 6, 6, 12);
            var caretInflated = ExpandRectMargins(caretBase, 24, 32, 24, 20);
            return UnionRects(uiaInflated, caretInflated);
        }

        return ExpandRectMargins(caretBase, 140, 180, 140, 96);
    }

    private static Rect UnionRects(Rect a, Rect b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        var x1 = Math.Min(a.Left, b.Left);
        var y1 = Math.Min(a.Top, b.Top);
        var x2 = Math.Max(a.Right, b.Right);
        var y2 = Math.Max(a.Bottom, b.Bottom);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect ExpandRectMargins(Rect r, double left, double top, double right, double bottom) =>
        new(r.Left - left, r.Top - top, r.Width + left + right, r.Height + top + bottom);

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
                _overlayWindow.CloseRequested += (s, e) =>
                {
                    _loggingService.Information("Overlay closed via close button — suppressing writer for 1 hour and requesting sleep");
                    SuppressFor(TimeSpan.FromHours(1));
                    // Notify ApplicationService so it can put the writer to sleep
                    // (so every keystroke no longer triggers the prediction pipeline).
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                };
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
