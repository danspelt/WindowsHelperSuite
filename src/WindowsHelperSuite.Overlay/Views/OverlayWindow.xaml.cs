using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Overlay.Views;

public partial class OverlayWindow : Window
{
    private List<SuggestionItem> _currentSuggestions = [];
    private int _currentPage = 0;
    private int _totalPages = 1;
    private OverlayLayout _currentLayout = OverlayLayout.Vertical;
    private OverlayLayout? _lockedLayout = null; // Prevents flipping while typing
    private DateTime _layoutLockTime = DateTime.MinValue;
    private readonly TimeSpan _layoutLockDuration = TimeSpan.FromSeconds(30); // Lock layout for 30s after typing starts
    private static readonly Duration OverlayRefreshDuration = new(TimeSpan.FromMilliseconds(90));
    private static readonly Duration SuggestionFadeDuration = new(TimeSpan.FromMilliseconds(85));
    private static readonly Duration SuggestionSlideDuration = new(TimeSpan.FromMilliseconds(90));

    public event EventHandler<int>? SuggestionSelected;
    public event EventHandler? NextPageRequested;
    public event EventHandler? PreviousPageRequested;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void ShowSuggestions(List<SuggestionItem> suggestions, int page = 0, int totalPages = 1)
    {
        _currentSuggestions = suggestions;
        _currentPage = page;
        _totalPages = totalPages;

        // Auto-detect layout if needed
        if (_currentLayout == OverlayLayout.Auto)
        {
            var detectedLayout = DetectOptimalLayout();

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

        if (suggestions.Count > 0)
        {
            var wasHidden = !IsVisible;
            Show();
            if (wasHidden)
            {
                Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80));
                BeginAnimation(OpacityProperty, fadeIn);
                AnimateOverlayRefresh();
            }
            else
            {
                AnimateOverlayRefresh();
            }

            AnimateSuggestionItems();
        }
        else
        {
            Hide();
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

    private OverlayLayout DetectOptimalLayout()
    {
        var screen = SystemParameters.WorkArea;
        var caretX = Left + (ActualWidth / 2); // Approximate center
        var caretY = Top;

        // Calculate available space in each direction
        var spaceBelow = screen.Bottom - caretY;
        var spaceAbove = caretY - screen.Top;
        var spaceRight = screen.Right - caretX;
        var spaceLeft = caretX - screen.Left;

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

        // Prefer horizontal for wide screens when centered
        if (screen.Width > screen.Height)
            horizontalScore += 1;

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
        Hide();
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
                FontSize = 12.5,
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
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 220 : 320
        };

        var textBlock = new TextBlock
        {
            Text = suggestion.DisplayText,
            FontSize = 17,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 220 : 320
        };

        textPanel.Children.Add(textBlock);

        var kindLabel = new TextBlock
        {
            Text = GetSuggestionKindLabel(suggestion.Kind),
            FontSize = 11,
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
            MaxWidth = _currentLayout == OverlayLayout.Horizontal ? 280 : 420,
            Margin = _currentLayout == OverlayLayout.Horizontal
                ? new Thickness(3, 0, 3, 0)
                : new Thickness(0, 3, 0, 3)
        };

        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = new TranslateTransform
        {
            X = _currentLayout == OverlayLayout.Horizontal ? 8 : 0,
            Y = _currentLayout == OverlayLayout.Vertical ? 8 : 0
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
                        TimeSpan.FromMilliseconds(90));
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

    public void PositionNearPoint(int caretX, int caretY, ScreenPosition preferredPosition)
    {
        UpdateLayout();

        // Ensure window is measured
        if (ActualWidth == 0 || ActualHeight == 0)
        {
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(0, 0, DesiredSize.Width, DesiredSize.Height));
            UpdateLayout();
        }

        var screen = SystemParameters.WorkArea;
        var measuredWidth = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        var measuredHeight = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        var windowWidth = measuredWidth > 0 ? measuredWidth : 600;
        var windowHeight = measuredHeight > 0 ? measuredHeight : 80;

        // RULE: Never cover the caret or active input line
        const double gapBelow = 24;
        const double gapAbove = 80;
        double left = caretX;
        double top;

        var spaceBelow = screen.Bottom - (caretY + gapBelow);
        var spaceAbove = caretY - screen.Top - gapAbove;

        if (preferredPosition == ScreenPosition.Above)
        {
            if (spaceAbove >= windowHeight)
            {
                top = caretY - windowHeight - gapAbove;
            }
            else if (spaceBelow >= windowHeight)
            {
                top = caretY + gapBelow;
            }
            else
            {
                top = screen.Bottom - windowHeight - 8;
            }
        }
        else if (preferredPosition == ScreenPosition.Below)
        {
            if (spaceBelow >= windowHeight)
            {
                top = caretY + gapBelow;
            }
            else if (spaceAbove >= windowHeight)
            {
                top = caretY - windowHeight - gapAbove;
            }
            else
            {
                top = screen.Bottom - windowHeight - 8;
            }
        }
        else
        {
            if (spaceAbove >= windowHeight)
            {
                top = caretY - windowHeight - gapAbove;
            }
            else if (spaceBelow >= windowHeight)
            {
                top = caretY + gapBelow;
            }
            else
            {
                top = screen.Bottom - windowHeight - 8;
            }
        }

        // Ensure window stays on screen horizontally
        left = Math.Max(screen.Left + 4, Math.Min(left, screen.Right - windowWidth - 4));
        top = Math.Max(screen.Top, Math.Min(top, screen.Bottom - windowHeight));

        Left = left;
        Top = top;
    }

    public void ApplyUiSettings(int fontSize, double opacity, bool largeTextMode,
        string accentColor = "#4ADE80", string bgColor = "#0F0F14",
        string cardColor = "#1E1F2A", string textColor = "#F0F0F5")
    {
        var baseFontSize = largeTextMode ? Math.Max(fontSize * 1.5, 20) : Math.Max(fontSize, 16);
        Opacity = Math.Clamp(opacity, 0.35, 1.0);

        ApplyColorResource("AccentGreen", accentColor);
        ApplyColorResource("AccentStripe", accentColor);
        ApplyColorResource("AccentGreenDim", DimColor(accentColor, 0.35));
        ApplyColorResource("PrimaryBackground", bgColor);
        ApplyColorResource("CardBackground", cardColor);
        ApplyColorResource("SecondaryBackground", cardColor);
        ApplyColorResource("TextPrimary", textColor);

        foreach (var child in SuggestionsContainer.Children.OfType<Button>())
        {
            child.FontSize = baseFontSize;
            var stackPanel = child.Content as StackPanel;
            if (stackPanel?.Children.Count > 1 && stackPanel.Children[1] is StackPanel textPanel)
            {
                if (textPanel.Children.Count > 0 && textPanel.Children[0] is TextBlock textBlock)
                {
                    textBlock.FontSize = baseFontSize;
                }

                if (textPanel.Children.Count > 1 && textPanel.Children[1] is TextBlock detailText)
                {
                    detailText.FontSize = Math.Max(baseFontSize - 5, 11);
                }
            }

            child.MinHeight = largeTextMode ? 64 : 48;
        }

        ContextLabel.FontSize = Math.Max(baseFontSize - 4, 12);
        PagingIndicator.FontSize = Math.Max(baseFontSize - 6, 11);
        SpeakerIndicator.FontSize = Math.Max(baseFontSize - 6, 11);
    }

    private void ApplyColorResource(string key, string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            Resources[key] = new System.Windows.Media.SolidColorBrush(color);
        }
        catch { }
    }

    private static string DimColor(string hex, double alpha)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return $"#{(byte)(color.R * alpha):X2}{(byte)(color.G * alpha):X2}{(byte)(color.B * alpha):X2}";
        }
        catch { return hex; }
    }

    private static string GetSuggestionKindLabel(SuggestionKind kind)
    {
        return kind switch
        {
            SuggestionKind.PhraseCompletion => "Phrase",
            SuggestionKind.NextWord => "Next word",
            SuggestionKind.UserHistory => "From history",
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

            var beginTime = TimeSpan.FromMilliseconds(Math.Min(index * 15, 75));
            var fadeAnimation = new DoubleAnimation(0, 1, SuggestionFadeDuration)
            {
                BeginTime = beginTime,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            button.BeginAnimation(OpacityProperty, fadeAnimation);

            if (button.RenderTransform is not TranslateTransform translateTransform)
            {
                continue;
            }

            var property = _currentLayout == OverlayLayout.Horizontal
                ? TranslateTransform.XProperty
                : TranslateTransform.YProperty;

            var fromOffset = _currentLayout == OverlayLayout.Horizontal ? 10d : 10d;
            var slideAnimation = new DoubleAnimation(fromOffset, 0, SuggestionSlideDuration)
            {
                BeginTime = beginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            translateTransform.BeginAnimation(property, slideAnimation);
        }
    }
}

public enum ScreenPosition
{
    Below,
    Above,
    Left,
    Right
}
