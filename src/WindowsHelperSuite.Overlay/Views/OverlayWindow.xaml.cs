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
    /// <summary>Raised when the user clicks the close button to dismiss the overlay.</summary>
    public event EventHandler? CloseRequested;

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
            if (wasHidden && _overlayFadeTransitionMs > 0)
            {
                Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_overlayFadeTransitionMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);
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
        const double horizontalHeight = 80;
        const double verticalWidth = 120;
        const double verticalHeight = 400;

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
        switch (_currentLayout)
        {
            case OverlayLayout.Horizontal:
                SuggestionsContainer.Orientation = Orientation.Horizontal;
                break;
            case OverlayLayout.Vertical:
                SuggestionsContainer.Orientation = Orientation.Vertical;
                break;
        }
    }

    private void RenderSuggestions()
    {
        SuggestionsContainer.Children.Clear();

        foreach (var suggestion in _currentSuggestions)
        {
            var button = CreateSuggestionButton(suggestion);
            SuggestionsContainer.Children.Add(button);
        }
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
            button.ApplyTemplate();
            if (button.Template?.FindName("border", button) is not System.Windows.Controls.Border border)
            {
                continue;
            }

            var selected = _highlightedVisualIndex == i;
            border.Background = (Brush)FindResource(selected ? "CardHover" : "CardBackground");
            border.BorderBrush = (Brush)FindResource(selected ? "AccentGreen" : "BorderSubtle");
            border.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void UpdatePagingIndicator()
    {
        if (_totalPages > 1)
        {
            PagingIndicator.Text = $"Page {_currentPage + 1} of {_totalPages}";
            PagingIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            PagingIndicator.Visibility = Visibility.Collapsed;
        }
    }

    public void SetLayout(OverlayLayout layout)
    {
        _currentLayout = layout;
        if (layout != OverlayLayout.Auto)
        {
            // Manual layout selection - don't lock, just apply
            _lockedLayout = null;
        }
        ApplyLayout();
    }

    public void SetContextMode(string? contextSummary, string? fullSentenceWords = null)
    {
        if (string.IsNullOrWhiteSpace(contextSummary) && string.IsNullOrWhiteSpace(fullSentenceWords))
        {
            ContextBanner.Visibility = Visibility.Collapsed;
            ContextBanner.ToolTip = null;
            return;
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(contextSummary))
        {
            lines.Add(contextSummary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(fullSentenceWords))
        {
            lines.Add($"Sentence: {fullSentenceWords.Trim()}");
        }

        ContextLabel.Text = string.Join(Environment.NewLine, lines);
        ContextBanner.ToolTip = string.IsNullOrWhiteSpace(fullSentenceWords)
            ? contextSummary?.Trim()
            : fullSentenceWords.Trim();
        ContextBanner.Visibility = Visibility.Visible;
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

    /// <summary>Non-blocking status line (e.g. AI unavailable). Pass null to clear.</summary>
    public void SetOverlayStatusHint(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            OverlayStatusHint.Visibility = Visibility.Collapsed;
            OverlayStatusHint.Text = "";
            return;
        }

        OverlayStatusHint.Text = message.Trim();
        OverlayStatusHint.Visibility = Visibility.Visible;
    }

    private Button CreateSuggestionButton(SuggestionItem suggestion)
    {
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Pill-shaped number badge
        var badgeBorder = new Border
        {
            Style = (Style)FindResource("SlotBadgeStyle"),
            Child = new TextBlock
            {
                Text = suggestion.Slot.ToString(),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x14)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        stackPanel.Children.Add(badgeBorder);

        var textPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 298 : 432
        };

        var textBlock = new TextBlock
        {
            Text = suggestion.DisplayText,
            FontSize = 20,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 298 : 432
        };

        textPanel.Children.Add(textBlock);

        var kindLabel = new TextBlock
        {
            Text = GetSuggestionKindLabel(suggestion.Kind),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.NoWrap
        };

        textPanel.Children.Add(kindLabel);
        stackPanel.Children.Add(textPanel);

        var button = new Button
        {
            Content = stackPanel,
            Tag = suggestion.Slot,
            Style = (Style)FindResource("SuggestionButtonStyle"),
            Opacity = 0,
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 360 : 528,
            ToolTip = BuildSuggestionToolTip(suggestion),
            Margin = _currentLayout == OverlayLayout.Horizontal
                ? new Thickness(5, 0, 5, 0)
                : new Thickness(0, 5, 0, 5)
        };

        // Store score for glow animation
        button.SetValue(AttachedSuggestionScoreProperty, suggestion.Score);

        // Apply glow effect to high-confidence suggestions (score > 3000)
        if (suggestion.Score > 3000)
        {
            ApplyGlowEffect(button, suggestion.Score);
        }

        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = new TranslateTransform
        {
            X = _currentLayout == OverlayLayout.Horizontal ? 14 : 0,
            Y = _currentLayout == OverlayLayout.Vertical ? 14 : 0
        };

        button.Click += (s, e) =>
        {
            if (s is Button btn && btn.Tag is int slot)
            {
                SuggestionSelected?.Invoke(this, slot);
            }
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
                var border = child.Template?.FindName("border", child) as System.Windows.Controls.Border;
                if (border != null)
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
        var display = spokenText.Length > 20 ? spokenText[..20] + "…" : spokenText;
        SpeakerIndicator.Text = $"\U0001F50A {display}";
        SpeakerIndicator.Opacity = 1.0;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(800))
        {
            BeginTime = TimeSpan.FromMilliseconds(400)
        };
        SpeakerIndicator.BeginAnimation(OpacityProperty, fadeOut);
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

    /// <param name="textExclusionBounds">
    /// Screen rectangle the overlay must not intersect (UIA text field union Win32 caret rect, inflated).
    /// If null, a conservative fallback around the caret is used.
    /// </param>
    public void PositionNearPoint(int caretX, int caretY, ScreenPosition preferredPosition, Rect? textExclusionBounds = null)
    {
        UpdateLayout();

        // Ensure window is measured
        if (ActualWidth == 0 || ActualHeight == 0)
        {
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(0, 0, DesiredSize.Width, DesiredSize.Height));
            UpdateLayout();
        }

        // Work area for the monitor that contains the caret (multi-monitor)
        double screenLeft;
        double screenTop;
        double screenRight;
        double screenBottom;
        if (Win32Screen.TryGetWorkAreaForPoint(caretX, caretY, out var wl, out var wt, out var wr, out var wb))
        {
            screenLeft = wl;
            screenTop = wt;
            screenRight = wr;
            screenBottom = wb;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            screenLeft = wa.Left;
            screenTop = wa.Top;
            screenRight = wa.Right;
            screenBottom = wa.Bottom;
        }

        var measuredWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        var measuredHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        var windowWidth = measuredWidth > 0 ? measuredWidth : 600;
        var windowHeight = measuredHeight > 0 ? measuredHeight : 80;

        var exclusion = ResolveExclusionBounds(caretX, caretY, textExclusionBounds);
        var fieldBottom = exclusion.Bottom;
        var fieldTop = exclusion.Top;

        const double sideGap = 20; // Increased gap for better separation
        const double placementGap = 15; // Increased placement gap
        double left = caretX;
        double top;
        double besideL;
        double besideT;

        // Space for overlay fully above/below the exclusion zone (including a larger gap for better text field protection)
        var spaceBelowField = screenBottom - (fieldBottom + placementGap);
        var spaceAboveField = (fieldTop - placementGap) - screenTop;

        if (preferredPosition == ScreenPosition.Left)
        {
            left = exclusion.Left - windowWidth - sideGap;
            top = exclusion.Top + (exclusion.Height - windowHeight) / 2;
        }
        else if (preferredPosition == ScreenPosition.Right)
        {
            left = exclusion.Right + sideGap;
            top = exclusion.Top + (exclusion.Height - windowHeight) / 2;
        }
        else if (preferredPosition == ScreenPosition.Above)
        {
            if (spaceAboveField >= windowHeight)
            {
                top = fieldTop - windowHeight - placementGap;
            }
            else if (spaceBelowField >= windowHeight)
            {
                top = fieldBottom + placementGap;
            }
            else if (TryDockBesideExclusion(exclusion, windowWidth, windowHeight, screenLeft, screenTop, screenRight, screenBottom, sideGap, out besideL, out besideT))
            {
                left = besideL;
                top = besideT;
            }
            else
            {
                top = screenBottom - windowHeight - 8;
            }
        }
        else if (preferredPosition == ScreenPosition.Below)
        {
            if (spaceBelowField >= windowHeight)
            {
                top = fieldBottom + placementGap;
            }
            else if (spaceAboveField >= windowHeight)
            {
                top = fieldTop - windowHeight - placementGap;
            }
            else if (TryDockBesideExclusion(exclusion, windowWidth, windowHeight, screenLeft, screenTop, screenRight, screenBottom, sideGap, out besideL, out besideT))
            {
                left = besideL;
                top = besideT;
            }
            else
            {
                top = screenBottom - windowHeight - 8;
            }
        }
        else
        {
            // Enhanced Auto positioning — prioritize side positioning to avoid text field coverage
            var canPlaceAbove = spaceAboveField >= windowHeight;
            var canPlaceBelow = spaceBelowField >= windowHeight;
            
            // First, try side positioning (left/right) as it's less likely to interfere with text input
            if (TryDockBesideExclusion(exclusion, windowWidth, windowHeight, screenLeft, screenTop, screenRight, screenBottom, sideGap, out besideL, out besideT))
            {
                left = besideL;
                top = besideT;
            }
            // If side positioning not available, try vertical positioning
            else if (canPlaceAbove && canPlaceBelow)
            {
                // Choose the position with more space, but prefer below for typical typing scenarios
                if (spaceBelowField > spaceAboveField + 40) // Increased threshold for stronger preference
                {
                    top = fieldBottom + placementGap;
                }
                else if (spaceAboveField > spaceBelowField + 40)
                {
                    top = fieldTop - windowHeight - placementGap;
                }
                else
                {
                    top = fieldBottom + placementGap; // Default to below when equal
                }
            }
            else if (canPlaceBelow)
            {
                top = fieldBottom + placementGap;
            }
            else if (canPlaceAbove)
            {
                top = fieldTop - windowHeight - placementGap;
            }
            else
            {
                // Last resort: position at bottom of screen with maximum gap from text field
                top = screenBottom - windowHeight - 20; // Increased bottom margin
            }
        }

        NudgeHorizontalClearOfExclusion(ref left, top, windowWidth, windowHeight, exclusion, screenLeft, screenRight, sideGap);

        left = Math.Max(screenLeft + 4, Math.Min(left, screenRight - windowWidth - 4));
        top = Math.Max(screenTop, Math.Min(top, screenBottom - windowHeight));

        ResolveExclusionOverlap(ref left, ref top, windowWidth, windowHeight, exclusion, screenLeft, screenTop, screenRight, screenBottom);

        Left = left;
        Top = top;
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
        double gap,
        out double left,
        out double top)
    {
        top = exclusion.Top + (exclusion.Height - windowHeight) / 2;
        top = Math.Max(screenTop, Math.Min(top, screenBottom - windowHeight));

        var rightDock = exclusion.Right + gap;
        if (rightDock + windowWidth <= screenRight)
        {
            left = rightDock;
            return true;
        }

        var leftDock = exclusion.Left - gap - windowWidth;
        if (leftDock >= screenLeft)
        {
            left = leftDock;
            return true;
        }

        left = 0;
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
        string accentColor = "#4ADE80", string bgColor = "#0F0F14",
        string cardColor = "#1E1F2A", string textColor = "#F0F0F5",
        string fontFamily = "Segoe UI", string fontWeight = "SemiBold",
        int overlayFadeTransitionMs = 110)
    {
        _overlayFadeTransitionMs = Math.Clamp(overlayFadeTransitionMs, 0, 600);
        var baseFontSize = largeTextMode ? Math.Max(fontSize * 1.5, 24) : Math.Max(fontSize, 19);
        // Opacity setting drives *surface* alpha so the caret / host text field shows through the panel.
        // Window stays at 1 so suggestion text and accents stay sharp (whole-window opacity mutes everything).
        var userOp = Math.Clamp(opacity, 0.10, 1.0);
        Opacity = 1.0;
        var shellAlpha = userOp;
        var cardAlpha = Math.Min(1.0, userOp + 0.28);
        var borderAlpha = Math.Min(1.0, userOp + 0.12);

        var ff = new System.Windows.Media.FontFamily(
            string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily);
        var fw = ParseFontWeight(fontWeight);

        ApplyColorResource("AccentGreen", accentColor);
        ApplyColorResource("AccentStripe", accentColor);
        ApplyColorResource("AccentGreenDim", DimColor(accentColor, 0.35));
        ApplyTranslucentPaint("PrimaryBackground", bgColor, shellAlpha);
        ApplyTranslucentPaint("ContextBackground", "#16171F", shellAlpha);
        ApplyTranslucentPaint("BorderColor", "#2A2B36", borderAlpha);
        ApplyTranslucentPaint("BorderSubtle", "#222330", borderAlpha);
        ApplyTranslucentPaint("CardBackground", cardColor, cardAlpha);
        ApplyTranslucentPaint("SecondaryBackground", DimColor(cardColor, 0.72), cardAlpha);
        ApplyTranslucentPaint("CardHover", BlendWithWhite(cardColor, 0.08), cardAlpha);
        ApplyColorResource("TextPrimary", textColor);

        FontFamily = ff;

        foreach (var child in SuggestionsContainer.Children.OfType<Button>())
        {
            child.FontSize = baseFontSize;
            child.FontFamily = ff;
            child.FontWeight = fw;
            var stackPanel = child.Content as StackPanel;
            if (stackPanel?.Children.Count > 1 && stackPanel.Children[1] is StackPanel textPanel)
            {
                if (textPanel.Children.Count > 0 && textPanel.Children[0] is TextBlock textBlock)
                {
                    textBlock.FontSize = baseFontSize;
                    textBlock.FontFamily = ff;
                    textBlock.FontWeight = fw;
                }

                if (textPanel.Children.Count > 1 && textPanel.Children[1] is TextBlock detailText)
                {
                    detailText.FontSize = Math.Max(baseFontSize - 5, 11);
                    detailText.FontFamily = ff;
                }
            }

            child.MinHeight = largeTextMode ? 77 : 58;
        }

        ContextLabel.FontSize = Math.Max(baseFontSize - 4, 12);
        ContextLabel.FontFamily = ff;
        PagingIndicator.FontSize = Math.Max(baseFontSize - 6, 11);
        SpeakerIndicator.FontSize = Math.Max(baseFontSize - 6, 11);
    }

    // ── Hover-reveal close button ──
    private void OverlayRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeCloseButton(1.0, 140);
    }

    private void OverlayRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        FadeCloseButton(0.0, 220);
    }

    private void FadeCloseButton(double targetOpacity, int durationMs)
    {
        if (CloseButton == null)
            return;

        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CloseButton.BeginAnimation(OpacityProperty, animation);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            Resources[key] = new System.Windows.Media.SolidColorBrush(color);
            Resources[$"{key}Color"] = color;
        }
        catch { }
    }

    /// <summary>RGB from <paramref name="hex"/> with A = round(255 × <paramref name="alpha01"/>).</summary>
    private void ApplyTranslucentPaint(string key, string hex, double alpha01)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(hex);
            var a = (byte)Math.Round(255.0 * Math.Clamp(alpha01, 0.08, 1.0));
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

    private static string GetSuggestionKindLabel(SuggestionKind kind)
    {
        return kind switch
        {
            SuggestionKind.PhraseCompletion => "Phrase",
            SuggestionKind.NextWord => "Next word",
            SuggestionKind.UserHistory => "From history",
            SuggestionKind.AiSuggestion => "AI",
            _ => "Word"
        };
    }

    private void AnimateOverlayRefresh()
    {
        OverlayBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        if (OverlayBorder.RenderTransform is not ScaleTransform scaleTransform)
        {
            scaleTransform = new ScaleTransform(1, 1);
            OverlayBorder.RenderTransform = scaleTransform;
        }

        var scaleX = new DoubleAnimation(0.985, 1, OverlayRefreshDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation(0.985, 1, OverlayRefreshDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
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
            button.ApplyTemplate();
            if (button.Template?.FindName("glowBorder", button) is not System.Windows.Controls.Border glowBorder)
            {
                return;
            }

            glowBorder.Opacity = 1;
            if (glowBorder.Effect is DropShadowEffect glowEffect)
            {
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
