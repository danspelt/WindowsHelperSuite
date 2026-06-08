using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.Overlay.Views;

public partial class OverlayWindow : Window
{
    // Attached property to store suggestion score for glow effects
    public static readonly DependencyProperty AttachedSuggestionScoreProperty =
        DependencyProperty.RegisterAttached("AttachedSuggestionScore",
            typeof(double), typeof(OverlayWindow),
            new PropertyMetadata(0.0));

    private List<SuggestionItem> _currentSuggestions = [];
    private int _currentPage = 0;
    private int _totalPages = 1;
    private OverlayLayout _currentLayout = OverlayLayout.Vertical;
    private OverlayLayout? _lockedLayout = null; // Prevents flipping while typing
    private DateTime _layoutLockTime = DateTime.MinValue;
    private readonly TimeSpan _layoutLockDuration = TimeSpan.FromSeconds(30); // Lock layout for 30s after typing starts
    private static readonly Duration OverlayRefreshDuration = new(TimeSpan.FromMilliseconds(100));
    private static readonly Duration SuggestionFadeDuration = new(TimeSpan.FromMilliseconds(100));
    private static readonly Duration SuggestionSlideDuration = new(TimeSpan.FromMilliseconds(115));
    private int _overlayFadeTransitionMs = 110;
    private bool _hideFadeInProgress;
    /// <summary>Visual index in <see cref="SuggestionsContainer"/> for keyboard highlight; null = none.</summary>
    private int? _highlightedVisualIndex;

    public event EventHandler<int>? SuggestionSelected;
    public event EventHandler? NextPageRequested;
    public event EventHandler? PreviousPageRequested;
    /// <summary>Display text of the suggestion now keyboard-highlighted.</summary>
    public event EventHandler<string?>? SuggestionHighlightChanged;
    /// <summary>Raised when the overlay requests dismissal. Kept for OverlayService compatibility.</summary>
#pragma warning disable CS0067
    public event EventHandler? CloseRequested;
#pragma warning restore CS0067

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void ShowSuggestions(
        List<SuggestionItem> suggestions,
        int page = 0,
        int totalPages = 1,
        int layoutCaretX = int.MinValue,
        int layoutCaretY = int.MinValue,
        int fadeTransitionMs = 110)
    {
        _overlayFadeTransitionMs = Math.Clamp(fadeTransitionMs, 0, 600);
        _currentSuggestions = suggestions;
        _currentPage = page;
        _totalPages = totalPages;

        // Auto-detect layout if needed
        if (_currentLayout == OverlayLayout.Auto)
        {
            var detectedLayout = DetectOptimalLayout(layoutCaretX, layoutCaretY);

            // Check if we should lock the layout
            if (_lockedLayout.HasValue && DateTime.Now - _layoutLockTime < _layoutLockDuration)
            {
                // Keep the locked layout
                _currentLayout = _lockedLayout.Value;
            }
            else
            {
                // Update layout and lock it
                _currentLayout = detectedLayout;
                _lockedLayout = detectedLayout;
                _layoutLockTime = DateTime.Now;
            }
        }

        ApplyLayout();
        RenderSuggestions();
        UpdatePagingIndicator();
        _highlightedVisualIndex = null;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, ApplySuggestionHighlight);

        if (suggestions.Count > 0)
        {
            if (_hideFadeInProgress)
            {
                BeginAnimation(OpacityProperty, null);
                _hideFadeInProgress = false;
            }

            var wasHidden = !IsVisible;
            Show();
            // Ensure window is active and visible
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            if (wasHidden && _overlayFadeTransitionMs > 0)
            {
                // Keep window visible, just animate refresh
                AnimateOverlayRefresh();
            }
            else
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                AnimateOverlayRefresh();
            }

            AnimateSuggestionItems();
        }
        else
        {
            HideSuggestionsAnimated();
        }
    }

    /// <summary>
    /// Resets the layout lock, allowing auto layout to re-detect on next show
    /// </summary>
    public void ResetLayoutLock()
    {
        _lockedLayout = null;
        _layoutLockTime = DateTime.MinValue;
    }

    private OverlayLayout DetectOptimalLayout(int refCaretX, int refCaretY)
    {
        double screenLeft;
        double screenTop;
        double screenRight;
        double screenBottom;
        var caretX = refCaretX;
        var caretY = refCaretY;
        if (caretX == int.MinValue || caretY == int.MinValue
                                   || !Win32Screen.TryGetWorkAreaForPoint(caretX, caretY, out var wl, out var wt, out var wr, out var wb))
        {
            var screen = SystemParameters.WorkArea;
            screenLeft = screen.Left;
            screenTop = screen.Top;
            screenRight = screen.Right;
            screenBottom = screen.Bottom;
            if (caretX == int.MinValue)
            {
                caretX = (int)(screenLeft + (screenRight - screenLeft) / 2);
            }

            if (caretY == int.MinValue)
            {
                caretY = (int)(screenTop + (screenBottom - screenTop) / 2);
            }
        }
        else
        {
            screenLeft = wl;
            screenTop = wt;
            screenRight = wr;
            screenBottom = wb;
        }

        // Calculate available space in each direction from the caret (or reference point)
        var spaceBelow = screenBottom - caretY;
        var spaceAbove = caretY - screenTop;
        var spaceRight = screenRight - caretX;
        var spaceLeft = caretX - screenLeft;

        // Estimate overlay sizes (approximate)
        const double horizontalHeight = 82;
        const double verticalWidth = 125;
        const double verticalHeight = 420;

        // Score each layout option based on available space
        var horizontalScore = 0;
        var verticalScore = 0;

        // Prefer horizontal if there's space below or above
        if (spaceBelow > horizontalHeight || spaceAbove > horizontalHeight)
            horizontalScore += 2;

        // Prefer vertical if there's space on sides AND room vertically
        if ((spaceRight > verticalWidth || spaceLeft > verticalWidth) && 
            (spaceBelow > verticalHeight || spaceAbove > verticalHeight))
            verticalScore += 2;

        // Prefer horizontal for wide work areas when centered
        if ((screenRight - screenLeft) > (screenBottom - screenTop))
        {
            horizontalScore += 1;
        }

        // Default to horizontal if scores are equal
        return horizontalScore >= verticalScore ? OverlayLayout.Horizontal : OverlayLayout.Vertical;
    }

    private void ApplyLayout()
    {
        // SuggestionsContainer is a WrapPanel — always horizontal, wraps automatically
    }

    private void RenderSuggestions()
    {
        SuggestionsContainer.Children.Clear();
        SentenceContainer.Children.Clear();

        var hasSentence = false;
        foreach (var suggestion in _currentSuggestions)
        {
            if (suggestion.Kind == SuggestionKind.AiSentence)
            {
                var btn = CreateSentenceButton(suggestion);
                SentenceContainer.Children.Add(btn);
                hasSentence = true;
            }
            else
            {
                var button = CreateSuggestionButton(suggestion);
                SuggestionsContainer.Children.Add(button);
            }
        }

        SentenceContainer.Visibility = hasSentence ? Visibility.Visible : Visibility.Collapsed;
        SentenceSeparator.Visibility = hasSentence ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button CreateSentenceButton(SuggestionItem suggestion)
    {
        var aiLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x60, 0x40, 0xCC)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = "AI",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xAA, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var sentenceText = new TextBlock
        {
            Text = suggestion.DisplayText,
            FontSize = 15,
            FontFamily = new FontFamily("Segoe UI"),
            FontStyle = FontStyles.Italic,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xCC, 0xBB, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 800
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(aiLabel);
        row.Children.Add(sentenceText);

        var sentenceBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        sentenceBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0x88, 0x44, 0xFF), 0));
        sentenceBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x12, 0x44, 0x22, 0xFF), 1));

        var pill = new Border
        {
            Background = sentenceBg,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x99, 0x77, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(2, 2, 2, 2),
            Padding = new Thickness(14, 8, 16, 8),
            Child = row
        };

        var button = new Button
        {
            Content = pill,
            Tag = suggestion.Slot,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Style = (Style)TryFindResource("PillButtonStyle")
        };

        button.MouseEnter += (_, _) =>
        {
            if (button.Content is Border b)
            {
                var hoverBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                hoverBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x44, 0xAA, 0x66, 0xFF), 0));
                hoverBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0x66, 0x33, 0xFF), 1));
                b.Background = hoverBg;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xBB, 0x88, 0xFF));
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Content is Border b)
            {
                var normalBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                normalBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0x88, 0x44, 0xFF), 0));
                normalBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x12, 0x44, 0x22, 0xFF), 1));
                b.Background = normalBg;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x99, 0x77, 0xFF));
            }
        };

        button.Click += (s, e) =>
        {
            if (s is Button btn && btn.Tag is int slot)
                SuggestionSelected?.Invoke(this, slot);
        };

        return button;
    }

    /// <summary>Moves keyboard highlight in list order (Up = earlier, Down = later); wraps within the page.</summary>
    public void MoveSuggestionHighlight(int delta)
    {
        var buttons = SuggestionsContainer.Children.OfType<Button>().ToList();
        var n = buttons.Count;
        if (n == 0)
        {
            return;
        }

        if (_highlightedVisualIndex == null)
        {
            _highlightedVisualIndex = delta > 0 ? 0 : n - 1;
        }
        else
        {
            _highlightedVisualIndex = ((_highlightedVisualIndex.Value + delta) % n + n) % n;
        }

        ApplySuggestionHighlight();
        RaiseSuggestionHighlightChanged();
    }

    /// <summary>Keyboard highlight slot for the current page (matches number-key pick), or null if none.</summary>
    public int? GetHighlightedSuggestionSlot()
    {
        if (_highlightedVisualIndex is not { } idx || idx < 0 || idx >= _currentSuggestions.Count)
        {
            return null;
        }

        return _currentSuggestions[idx].Slot;
    }

    private void RaiseSuggestionHighlightChanged()
    {
        if (_highlightedVisualIndex is not { } idx || idx < 0 || idx >= _currentSuggestions.Count)
        {
            return;
        }

        SuggestionHighlightChanged?.Invoke(this, _currentSuggestions[idx].DisplayText);
    }

    private void ApplySuggestionHighlight()
    {
        var buttons = SuggestionsContainer.Children.OfType<Button>().ToList();
        var n = buttons.Count;
        for (var i = 0; i < n; i++)
        {
            var button = buttons[i];
            if (GetSuggestionBorder(button) is not System.Windows.Controls.Border border)
            {
                continue;
            }

            var selected = _highlightedVisualIndex == i;
            if (selected)
            {
                var selBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                selBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0x4A, 0xDE, 0x80), 0));
                selBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0x00, 0xAA, 0x55), 1));
                border.Background = selBg;
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x4A, 0xDE, 0x80));
                border.BorderThickness = new Thickness(1.5);
                border.Effect = new DropShadowEffect { Color = Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.5 };
            }
            else
            {
                var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0));
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0x88, 0xBB, 0xFF), 1));
                border.Background = bg;
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                border.BorderThickness = new Thickness(1);
                border.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.3 };
            }
        }
    }

    private System.Windows.Controls.Border? GetSuggestionBorder(Button button)
    {
        return button.Content as System.Windows.Controls.Border;
    }

    private System.Windows.Controls.Border? GetGlowBorder(Button button)
    {
        if (button.Template?.FindName("glowBorder", button) is System.Windows.Controls.Border existing)
        {
            return existing;
        }

        if (!button.IsInitialized)
        {
            return null;
        }

        button.ApplyTemplate();
        return button.Template?.FindName("glowBorder", button) as System.Windows.Controls.Border;
    }

    private void UpdatePagingIndicator()
    {
        // Footer removed — paging still works via hotkeys; no on-screen page label.
    }

    public OverlayLayout CurrentLayout => _currentLayout;

    public void SetLayout(OverlayLayout layout)
    {
        _currentLayout = layout;
        if (layout != OverlayLayout.Auto)
        {
            _lockedLayout = layout;
            _layoutLockTime = DateTime.Now;
        }
        else
        {
            _lockedLayout = null;
        }

        ApplyLayout();
        if (IsVisible && _currentSuggestions.Count > 0)
        {
            RenderSuggestions();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, ApplySuggestionHighlight);
        }
    }

    public void SetContextMode(string? contextSummary, string? fullSentenceWords = null)
    {
        // Sentence/context banner removed from overlay UI.
    }

    public void HideSuggestions()
    {
        HideSuggestionsAnimated();
    }

    private void HideSuggestionsAnimated()
    {
        if (!IsVisible)
        {
            return;
        }

        if (_overlayFadeTransitionMs <= 0)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
            return;
        }

        _hideFadeInProgress = true;
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(_overlayFadeTransitionMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            _hideFadeInProgress = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>Non-blocking status line — UI element removed, no-op.</summary>
    public void SetOverlayStatusHint(string? message)
    {
        // Status hint UI removed - minimal overlay shows only words
    }

    private Button CreateSuggestionButton(SuggestionItem suggestion)
    {
        // Number badge — small, subtle, top-left
        var numBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0x4A, 0xDE, 0x80)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = suggestion.Slot.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0xFF, 0xE8)),
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        // Word text — large, white
        var wordText = new TextBlock
        {
            Text = suggestion.DisplayText,
            FontSize = 22,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Row
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(numBadge);
        row.Children.Add(wordText);

        // Pill border
        var pillBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        pillBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0));
        pillBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0x88, 0xBB, 0xFF), 1));

        var pill = new Border
        {
            Background = pillBg,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(5, 4, 5, 4),
            Padding = new Thickness(14, 8, 16, 8),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.3 },
            Child = row
        };

        // Button wrapper
        var button = new Button
        {
            Content = pill,
            Tag = suggestion.Slot,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Opacity = 1,
            Style = (Style)TryFindResource("PillButtonStyle")
        };

        button.RenderTransform = new TranslateTransform(0, 0);

        // Hover effects
        button.MouseEnter += (_, _) =>
        {
            if (GetSuggestionBorder(button) is { } b)
            {
                var hoverBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                hoverBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0));
                hoverBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0x88, 0xCC, 0xFF), 1));
                b.Background = hoverBg;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                b.Effect = new DropShadowEffect { Color = Color.FromArgb(0xFF, 0x88, 0xCC, 0xFF), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.4 };
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (GetSuggestionBorder(button) is { } b)
            {
                var normalBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                normalBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0));
                normalBg.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0x88, 0xBB, 0xFF), 1));
                b.Background = normalBg;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                b.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.3 };
            }
        };

        button.Click += (s, e) =>
        {
            if (s is Button btn && btn.Tag is int slot)
                SuggestionSelected?.Invoke(this, slot);
        };

        return button;
    }

    public void HandleSelectionKey(int slot)
    {
        var suggestion = _currentSuggestions.FirstOrDefault(s => s.Slot == slot);
        if (suggestion != null)
        {
            FlashSelection(slot);
            SuggestionSelected?.Invoke(this, slot);
        }
    }

    /// <summary>
    /// Brief green flash on the selected suggestion button for instant visual feedback.
    /// </summary>
    public void FlashSelection(int slot)
    {
        foreach (var child in SuggestionsContainer.Children.OfType<Button>())
        {
            if (child.Tag is int btnSlot && btnSlot == slot)
            {
                if (GetSuggestionBorder(child) is System.Windows.Controls.Border border)
                {
                    var flash = new ColorAnimation(
                        Color.FromRgb(0x4A, 0xDE, 0x80), // AccentGreen
                        Color.FromRgb(0x1E, 0x1F, 0x2A), // Back to CardBackground
                        TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    border.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x2A));
                    ((SolidColorBrush)border.Background).BeginAnimation(SolidColorBrush.ColorProperty, flash);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Briefly show a speaker indicator with the spoken text, fades out after 800ms.
    /// </summary>
    public void ShowSpeakerIndicator(string spokenText)
    {
        // Footer removed — TTS still plays; no speaker chip in the overlay.
    }

    public void HandleNextPage()
    {
        if (_currentPage < _totalPages - 1)
        {
            NextPageRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void HandlePreviousPage()
    {
        if (_currentPage > 0)
        {
            PreviousPageRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Shows the window immediately (for when Writer is woken but no suggestions yet).
    /// </summary>
    public void ShowWindow()
    {
        Show();
        Topmost = true;
        Activate();
    }

    /// <summary>
    /// Positions the overlay near the caret point. As an overlay, it follows the caret.
    /// </summary>
    public void PositionNearPoint(int caretX, int caretY, ScreenPosition preferredPosition, Rect? textExclusionBounds = null)
    {
        // Calculate window size (use desired or actual)
        var windowWidth = Width;
        var windowHeight = Height;
        if (double.IsNaN(windowWidth) || windowWidth <= 0)
            windowWidth = 320;
        if (double.IsNaN(windowHeight) || windowHeight <= 0)
            windowHeight = 140;

        // Get screen metrics
        double screenLeft, screenTop, screenRight, screenBottom;
        if (Win32Screen.TryGetWorkAreaForPoint(caretX, caretY, out var wl, out var wt, out var wr, out var wb))
        {
            screenLeft = wl;
            screenTop = wt;
            screenRight = wr;
            screenBottom = wb;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            screenLeft = area.Left;
            screenTop = area.Top;
            screenRight = area.Right;
            screenBottom = area.Bottom;
        }

        // Try positioning beside text exclusion area (text field) if provided
        if (textExclusionBounds.HasValue &&
            TryDockBesideExclusion(textExclusionBounds.Value, windowWidth, windowHeight,
                screenLeft, screenTop, screenRight, screenBottom, out var dockedX, out var dockedY))
        {
            Left = dockedX;
            Top = dockedY;
            return;
        }

        // Default: position based on preferred position and caret location
        int x, y;
        const int margin = 8;

        switch (preferredPosition)
        {
            case ScreenPosition.Below:
                x = caretX;
                y = caretY + margin;
                // Ensure stays on screen
                if (x + windowWidth > screenRight)
                    x = (int)(screenRight - windowWidth - margin);
                if (y + windowHeight > screenBottom)
                    y = caretY - (int)windowHeight - margin; // Flip to above
                break;

            case ScreenPosition.Above:
                x = caretX;
                y = caretY - (int)windowHeight - margin;
                if (y < screenTop)
                    y = caretY + margin; // Flip to below
                break;

            case ScreenPosition.Right:
                x = caretX + margin;
                y = caretY - (int)(windowHeight / 2);
                if (x + windowWidth > screenRight)
                    x = caretX - (int)windowWidth - margin; // Flip to left
                break;

            case ScreenPosition.Left:
                x = caretX - (int)windowWidth - margin;
                y = caretY - (int)(windowHeight / 2);
                if (x < screenLeft)
                    x = caretX + margin; // Flip to right
                break;

            default:
                x = caretX;
                y = caretY + margin;
                break;
        }

        // Final bounds clamp
        if (x < screenLeft) x = (int)screenLeft + margin;
        if (y < screenTop) y = (int)screenTop + margin;
        if (x + windowWidth > screenRight) x = (int)(screenRight - windowWidth - margin);
        if (y + windowHeight > screenBottom) y = (int)(screenBottom - windowHeight - margin);

        Left = x;
        Top = y;
    }

    /// <summary>Places the overlay entirely to the right or left of <paramref name="exclusion"/> when there is room.</summary>
    private static bool TryDockBesideExclusion(
        Rect exclusion,
        double windowWidth,
        double windowHeight,
        double screenLeft,
        double screenTop,
        double screenRight,
        double screenBottom,
        out int x,
        out int y)
    {
        const int margin = 12;
        x = 0;
        y = 0;

        // Try right side of exclusion
        var rightX = exclusion.Right + margin;
        var rightY = exclusion.Top + (exclusion.Height - windowHeight) / 2;

        if (rightX + windowWidth <= screenRight && rightY >= screenTop && rightY + windowHeight <= screenBottom)
        {
            x = (int)rightX;
            y = (int)Math.Max(screenTop + margin, Math.Min(rightY, screenBottom - windowHeight - margin));
            return true;
        }

        // Try left side of exclusion
        var leftX = exclusion.Left - windowWidth - margin;
        var leftY = exclusion.Top + (exclusion.Height - windowHeight) / 2;

        if (leftX >= screenLeft && leftY >= screenTop && leftY + windowHeight <= screenBottom)
        {
            x = (int)leftX;
            y = (int)Math.Max(screenTop + margin, Math.Min(leftY, screenBottom - windowHeight - margin));
            return true;
        }

        return false;
    }

    /// <summary>When vertically placed, shift horizontally off <paramref name="exclusion"/> if the window still spans it in X.</summary>
    private static void NudgeHorizontalClearOfExclusion(
        ref double left,
        double top,
        double windowWidth,
        double windowHeight,
        Rect exclusion,
        double screenLeft,
        double screenRight,
        double gap)
    {
        var r = new Rect(left, top, windowWidth, windowHeight);
        if (!r.IntersectsWith(exclusion))
        {
            return;
        }

        var rightDock = exclusion.Right + gap;
        if (rightDock + windowWidth <= screenRight)
        {
            left = rightDock;
            return;
        }

        var leftDock = exclusion.Left - gap - windowWidth;
        if (leftDock >= screenLeft)
        {
            left = leftDock;
        }
    }

    private static Rect ResolveExclusionBounds(int caretX, int caretY, Rect? textExclusionBounds)
    {
        if (textExclusionBounds is { IsEmpty: false } r)
        {
            return r;
        }

        // Enhanced text field detection with larger exclusion zone to ensure overlay never blocks input
        // Calculate a more conservative exclusion zone based on typical text field dimensions
        const int fieldWidth = 600;  // Wider to accommodate larger text fields
        const int fieldHeight = 300; // Taller to accommodate multi-line text fields
        
        // Center the exclusion zone around the caret position
        var left = caretX - (fieldWidth / 2);
        var top = caretY - 50; // Position caret near top of exclusion zone (typical for single-line fields)
        
        // Ensure the exclusion zone doesn't extend beyond reasonable text field boundaries
        // For multi-line fields, the caret might be deeper, so adjust accordingly
        if (caretY > top + 100) // If caret is deeper than expected, it might be a multi-line field
        {
            top = caretY - 150; // Adjust for multi-line scenarios
        }
        
        return new Rect(left, top, fieldWidth, fieldHeight);
    }

    private static void ResolveExclusionOverlap(
        ref double left,
        ref double top,
        double windowWidth,
        double windowHeight,
        Rect exclusion,
        double screenLeft,
        double screenTop,
        double screenRight,
        double screenBottom)
    {
        const double gap = 20; // Increased gap for better text field protection

        static bool Overlaps(double lx, double ty, double ww, double wh, Rect ex) =>
            new Rect(lx, ty, ww, wh).IntersectsWith(ex);

        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Priority 1: Position below the text field (preferred for typing scenarios)
        var topBelow = exclusion.Bottom + gap;
        if (topBelow + windowHeight <= screenBottom)
        {
            top = topBelow;
        }

        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Priority 2: Position to the right of the text field
        var leftRight = exclusion.Right + gap;
        if (leftRight + windowWidth <= screenRight)
        {
            left = leftRight;
            // Center vertically with the text field
            var centerY = exclusion.Top + (exclusion.Height - windowHeight) / 2;
            centerY = Math.Max(screenTop, Math.Min(centerY, screenBottom - windowHeight));
            top = centerY;
        }

        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Priority 3: Position to the left of the text field
        var leftLeft = exclusion.Left - windowWidth - gap;
        if (leftLeft >= screenLeft)
        {
            left = leftLeft;
            // Center vertically with the text field
            var centerY = exclusion.Top + (exclusion.Height - windowHeight) / 2;
            centerY = Math.Max(screenTop, Math.Min(centerY, screenBottom - windowHeight));
            top = centerY;
        }

        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Priority 4: Position above the text field
        var topAbove = exclusion.Top - windowHeight - gap;
        if (topAbove >= screenTop)
        {
            top = topAbove;
        }

        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Last resort: Position at screen edges with maximum distance from text field
        // Try bottom-right corner first
        left = screenRight - windowWidth - 20;
        top = screenBottom - windowHeight - 20;
        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Try bottom-left corner
        left = screenLeft + 20;
        top = screenBottom - windowHeight - 20;
        if (!Overlaps(left, top, windowWidth, windowHeight, exclusion))
        {
            return;
        }

        // Final fallback: use work area clamp but ensure we don't overlap
        var cl = Math.Max(screenLeft + 20, Math.Min(left, screenRight - windowWidth - 20));
        var ct = Math.Max(screenTop + 20, Math.Min(top, screenBottom - windowHeight - 20));
        if (!Overlaps(cl, ct, windowWidth, windowHeight, exclusion))
        {
            left = cl;
            top = ct;
        }
    }

    public void ApplyUiSettings(int fontSize, double opacity, bool largeTextMode,
        string accentColor = "#00FF00", string bgColor = "#000A0F0A",
        string cardColor = "#0A1F3A1F", string textColor = "#E0FFE0",
        string fontFamily = "Segoe UI", string fontWeight = "SemiBold",
        int overlayFadeTransitionMs = 110)
    {
        _overlayFadeTransitionMs = Math.Clamp(overlayFadeTransitionMs, 0, 600);
        var baseFontSize = largeTextMode ? Math.Max(fontSize * 1.35, 24) : Math.Max(fontSize, 20);
        // Opacity applies to word bubbles only; the overlay shell is always fully transparent.
        var userOp = Math.Clamp(opacity, 0.08, 1.0);
        Opacity = 1.0;
        var cardAlpha = userOp * 0.72;

        var ff = new System.Windows.Media.FontFamily(
            string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily);
        var fw = ParseFontWeight(fontWeight);

        // Glass theme - transparent background, no colored fill
        Background = System.Windows.Media.Brushes.Transparent;

        FontFamily = ff;

        foreach (var child in SuggestionsContainer.Children.OfType<Button>())
        {
            child.FontFamily = ff;
            child.FontWeight = fw;
            // pill Border > StackPanel row > [badge Border, TextBlock]
            if (child.Content is System.Windows.Controls.Border pill
                && pill.Child is StackPanel row
                && row.Children.Count > 1
                && row.Children[1] is TextBlock wordText)
            {
                wordText.FontSize = baseFontSize;
                wordText.FontFamily = ff;
                wordText.FontWeight = fw;
            }

            child.MinHeight = largeTextMode ? 56 : 44;
        }
    }

    /// <summary>Whether screen coordinates (physical pixels) fall inside this window.</summary>
    public bool ContainsScreenPoint(int screenX, int screenY)
    {
        if (!IsVisible)
        {
            return false;
        }

        try
        {
            var local = PointFromScreen(new Point(screenX, screenY));
            const double pad = 10;
            return local.X >= -pad
                   && local.Y >= -pad
                   && local.X <= ActualWidth + pad
                   && local.Y <= ActualHeight + pad;
        }
        catch
        {
            return false;
        }
    }


    private static FontWeight ParseFontWeight(string? weight) => (weight?.Trim().ToLowerInvariant()) switch
    {
        "thin"       => FontWeights.Thin,
        "extralight" => FontWeights.ExtraLight,
        "light"      => FontWeights.Light,
        "normal"     => FontWeights.Normal,
        "medium"     => FontWeights.Medium,
        "semibold"   => FontWeights.SemiBold,
        "bold"       => FontWeights.Bold,
        "extrabold"  => FontWeights.ExtraBold,
        "black"      => FontWeights.Black,
        _            => FontWeights.SemiBold,
    };

    private void ApplyColorResource(string key, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Resources[key] = new SolidColorBrush(color);
            if (Resources.Contains($"{key}Color"))
            {
                Resources[$"{key}Color"] = color;
            }
        }
        catch
        {
            // keep defaults from XAML
        }
    }

    /// <summary>RGB from <paramref name="hex"/> with A = round(255 × <paramref name="alpha01"/>).</summary>
    private void ApplyTranslucentPaint(string key, string hex, double alpha01)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(hex);
            var a = (byte)Math.Round(255.0 * Math.Clamp(alpha01, 0.04, 1.0));
            var color = Color.FromArgb(a, parsed.R, parsed.G, parsed.B);
            Resources[key] = new SolidColorBrush(color);
            Resources[$"{key}Color"] = color;
        }
        catch { }
    }

    private static string DimColor(string hex, double alpha)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return $"#{(byte)Math.Clamp(color.R * alpha, 0, 255):X2}{(byte)Math.Clamp(color.G * alpha, 0, 255):X2}{(byte)Math.Clamp(color.B * alpha, 0, 255):X2}";
        }
        catch { return hex; }
    }

    /// <summary>Lighten a hex color slightly for hover states (mix toward white).</summary>
    private static string BlendWithWhite(string hex, double amount)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            amount = Math.Clamp(amount, 0, 1);
            var r = (byte)(color.R + (255 - color.R) * amount);
            var g = (byte)(color.G + (255 - color.G) * amount);
            var b = (byte)(color.B + (255 - color.B) * amount);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return hex; }
    }

    private static string BuildSuggestionToolTip(SuggestionItem suggestion)
    {
        var t = suggestion.DisplayText.Trim();
        if (t.Length > 80)
        {
            t = t[..80] + "…";
        }

        return $"Key {suggestion.Slot} — insert \"{t}\"";
    }

    private void AnimateOverlayRefresh()
    {
        // Overlay border removed - no refresh animation needed
    }

    private void AnimateSuggestionItems()
    {
        for (var index = 0; index < SuggestionsContainer.Children.Count; index++)
        {
            if (SuggestionsContainer.Children[index] is not Button button)
            {
                continue;
            }

            var beginTime = TimeSpan.FromMilliseconds(Math.Min(index * 20, 100));
            var fadeAnimation = new DoubleAnimation(0, 1, SuggestionFadeDuration)
            {
                BeginTime = beginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            button.BeginAnimation(OpacityProperty, fadeAnimation);

            if (button.RenderTransform is not TranslateTransform translateTransform)
            {
                continue;
            }

            var property = _currentLayout == OverlayLayout.Horizontal
                ? TranslateTransform.XProperty
                : TranslateTransform.YProperty;

            var fromOffset = 12d;
            var slideAnimation = new DoubleAnimation(fromOffset, 0, SuggestionSlideDuration)
            {
                BeginTime = beginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            translateTransform.BeginAnimation(property, slideAnimation);
        }
    }

    private void ApplyGlowEffect(Button button, double score)
    {
        // Higher score = stronger glow (score 3000-5000 range)
        var intensity = Math.Min((score - 3000) / 2000.0, 1.0); // 0.0 to 1.0
        var blurRadius = 15 + (intensity * 15); // 15-30
        var opacity = 0.2 + (intensity * 0.25); // 0.2-0.45

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (GetGlowBorder(button) is not System.Windows.Controls.Border glowBorder)
            {
                return;
            }

            glowBorder.Opacity = 1;
            if (glowBorder.Effect is DropShadowEffect glowEffect)
            {
                if (Resources["AccentGreenColor"] is Color accent)
                {
                    glowEffect.Color = accent;
                }

                glowEffect.BlurRadius = blurRadius;
                glowEffect.Opacity = opacity;

                // Animate the glow intensity
                var pulseAnimation = new DoubleAnimation
                {
                    From = opacity * 0.7,
                    To = opacity,
                    Duration = TimeSpan.FromMilliseconds(1500),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                glowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, pulseAnimation);
            }
        });
    }
}

public enum ScreenPosition
{
    /// <summary>Prefer above caret, then below (legacy default).</summary>
    Auto,

    Below,
    Above,
    Left,
    Right
}
