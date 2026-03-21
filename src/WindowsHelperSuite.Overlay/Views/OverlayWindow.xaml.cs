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
            // Subtle fade-in (100ms max) — only on first show
            if (wasHidden)
            {
                Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80));
                BeginAnimation(OpacityProperty, fadeIn);
            }
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

    public void HideSuggestions()
    {
        Hide();
    }

    private Button CreateSuggestionButton(SuggestionItem suggestion)
    {
        // Create the content with slot number and text
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Slot number badge — prominent, high contrast
        var badgeBorder = new Border
        {
            Style = (Style)FindResource("SlotBadgeStyle"),
            Child = new TextBlock
            {
                Text = suggestion.Slot.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        stackPanel.Children.Add(badgeBorder);

        // Suggestion text — 16px minimum
        var textBlock = new TextBlock
        {
            Text = suggestion.DisplayText,
            FontSize = 16,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center
        };

        stackPanel.Children.Add(textBlock);

        var button = new Button
        {
            Content = stackPanel,
            Tag = suggestion.Slot,
            Style = (Style)FindResource("SuggestionButtonStyle"),
            Margin = _currentLayout == OverlayLayout.Horizontal
                ? new Thickness(4, 0, 4, 0)
                : new Thickness(0, 4, 0, 4)
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
            SuggestionSelected?.Invoke(this, slot);
        }
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

    public void ApplyUiSettings(int fontSize, double opacity, bool largeTextMode)
    {
        var baseFontSize = largeTextMode ? Math.Max(fontSize * 1.5, 20) : Math.Max(fontSize, 16);

        foreach (var child in SuggestionsContainer.Children.OfType<Button>())
        {
            child.FontSize = baseFontSize;
            var stackPanel = child.Content as StackPanel;
            if (stackPanel?.Children[1] is TextBlock textBlock)
            {
                textBlock.FontSize = baseFontSize;
            }
            child.MinHeight = largeTextMode ? 52 : 44;
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
