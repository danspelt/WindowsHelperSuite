using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using HotkeyBinding = WindowsHelperSuite.Core.Models.KeyBinding;

namespace WindowsHelperSuite.App;

public partial class HotkeySettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly Action _onSaved;

    // ── Appearance pending state ──
    private int _pendingFontSize;
    private double _pendingOpacity;
    private bool _pendingLargeText;
    private OverlayLayout _pendingLayout;
    private string _pendingAccent = "#4ADE80";
    private string _pendingBg = "#0F0F14";
    private string _pendingCard = "#1E1F2A";
    private string _pendingText = "#F0F0F5";
    private bool _suppressColorEvents;

    private static readonly string[] AccentPresets =
        ["#4ADE80", "#60A5FA", "#A78BFA", "#FB923C", "#F472B6", "#F87171", "#FACC15", "#34D399"];

    private static readonly string[] BgPresets =
        ["#0F0F14", "#080810", "#0A0A1E", "#0F172A", "#111827", "#1A1A1A", "#0D1117", "#141420"];

    private static readonly string[] CardPresets =
        ["#1E1F2A", "#1E293B", "#252525", "#1A1B2E", "#1F2937", "#222222", "#1C2333", "#202030"];

    private static readonly string[] TextPresets =
        ["#F0F0F5", "#E5E7EB", "#D1D5DB", "#FFFFFF", "#C9D1D9", "#F8F8F2", "#E2E8F0", "#CBD5E1"];

    private static readonly (string ActionName, string DisplayName, string Icon)[] KnownActions =
    [
        ("VolumeUp",                   "Volume Up",                    "🔊"),
        ("VolumeDown",                 "Volume Down",                  "🔉"),
        ("VolumeMute",                 "Mute / Unmute",                "🔇"),
        ("WriterRefresh",              "Refresh Suggestions",          "🔄"),
        ("ToggleOverlay",              "Show / Hide Overlay",          "💬"),
        ("PauseWriter",                "Pause / Resume Writer",        "⏸"),
        ("AddToWordBank",              "Add Word to Bank",             "📝"),
        ("AddPhraseToWordBank",        "Add Phrase to Bank",           "📋"),
        ("FixClipboardCapitalization", "Fix Clipboard Capitalization", "✏"),
        ("OpenModeMenu",               "Open Mode Menu",               "☰"),
    ];

    private static readonly Dictionary<string, string> DefaultGestures = new()
    {
        ["VolumeUp"]                   = "Ctrl+Shift+Up",
        ["VolumeDown"]                 = "Ctrl+Shift+Down",
        ["VolumeMute"]                 = "Ctrl+Shift+M",
        ["WriterRefresh"]              = "`",
        ["ToggleOverlay"]              = "Ctrl+Shift+O",
        ["PauseWriter"]                = "Ctrl+Shift+P",
        ["AddToWordBank"]              = "Ctrl+`",
        ["AddPhraseToWordBank"]        = "Ctrl+Shift+`",
        ["FixClipboardCapitalization"] = "Ctrl+Shift+C",
        ["OpenModeMenu"]               = "Ctrl+F3",
    };

    private readonly Dictionary<string, string> _pending = new();
    private string? _listeningAction;

    private readonly Dictionary<string, (Border Chip, TextBlock ChipText, System.Windows.Controls.Button ChangeBtn)> _rowRefs = new();

    public HotkeySettingsWindow(ISettingsService settingsService, Action onSaved)
    {
        _settingsService = settingsService;
        _onSaved = onSaved;
        InitializeComponent();
        LoadBindings();
        LoadAppearance();
        Loaded += (_, _) =>
        {
            BuildRows();
            BuildAppearanceTab();
        };
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // ═══════════════ APPEARANCE ═══════════════

    private void LoadAppearance()
    {
        var ui = _settingsService.Settings.Ui;
        _pendingFontSize = Math.Clamp(ui.FontSize, 8, 36);
        _pendingOpacity  = Math.Clamp(ui.Opacity * 100, 25, 100);
        _pendingLargeText = ui.LargeTextMode;
        _pendingLayout = ui.Layout;
        _pendingAccent = ui.AccentColor;
        _pendingBg     = ui.OverlayBackgroundColor;
        _pendingCard   = ui.CardColor;
        _pendingText   = ui.TextColor;
    }

    private void BuildAppearanceTab()
    {
        _suppressColorEvents = true;

        FontSizeSlider.Value = _pendingFontSize;
        FontSizeLabel.Text   = _pendingFontSize.ToString();

        OpacitySlider.Value = _pendingOpacity;
        OpacityLabel.Text   = $"{(int)_pendingOpacity}%";

        LargeTextCheck.IsChecked   = _pendingLargeText;
        LayoutVertical.IsChecked   = _pendingLayout == OverlayLayout.Vertical;
        LayoutHorizontal.IsChecked = _pendingLayout == OverlayLayout.Horizontal;

        LargeTextCheck.Checked   += (_, _) => _pendingLargeText = true;
        LargeTextCheck.Unchecked += (_, _) => _pendingLargeText = false;
        LayoutVertical.Checked   += (_, _) => _pendingLayout = OverlayLayout.Vertical;
        LayoutHorizontal.Checked += (_, _) => _pendingLayout = OverlayLayout.Horizontal;

        BuildSwatches(AccentSwatches, AccentPresets, AccentHex, AccentPreview, v => _pendingAccent = v);
        BuildSwatches(BgSwatches,     BgPresets,     BgHex,     BgPreview,     v => _pendingBg     = v);
        BuildSwatches(CardSwatches,   CardPresets,   CardHex,   CardPreview,   v => _pendingCard   = v);
        BuildSwatches(TextSwatches,   TextPresets,   TextHex,   TextPreview,   v => _pendingText   = v);

        SetHexAndPreview(AccentHex, AccentPreview, _pendingAccent);
        SetHexAndPreview(BgHex,     BgPreview,     _pendingBg);
        SetHexAndPreview(CardHex,   CardPreview,   _pendingCard);
        SetHexAndPreview(TextHex,   TextPreview,   _pendingText);

        _suppressColorEvents = false;
    }

    private static void BuildSwatches(WrapPanel panel, string[] presets,
        System.Windows.Controls.TextBox hexBox, Border preview, Action<string> setter)
    {
        panel.Children.Clear();
        foreach (var hex in presets)
        {
            var capturedHex = hex;
            var swatch = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex,
                Background = TryParseBrush(hex),
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                setter(capturedHex);
                SetHexAndPreview(hexBox, preview, capturedHex);
            };
            panel.Children.Add(swatch);
        }
    }

    private static void SetHexAndPreview(System.Windows.Controls.TextBox hexBox, Border preview, string hex)
    {
        hexBox.Text = hex;
        preview.Background = TryParseBrush(hex);
    }

    private static SolidColorBrush TryParseBrush(string hex)
    {
        try
        {
            var color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(System.Windows.Media.Colors.Transparent);
        }
    }

    private static bool IsValidHex(string hex) =>
        !string.IsNullOrWhiteSpace(hex) &&
        (hex.Length == 7 || hex.Length == 9) &&
        hex.StartsWith('#');

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _pendingFontSize = (int)e.NewValue;
        if (FontSizeLabel != null) FontSizeLabel.Text = _pendingFontSize.ToString();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _pendingOpacity = e.NewValue;
        if (OpacityLabel != null) OpacityLabel.Text = $"{(int)_pendingOpacity}%";
    }

    private void AccentHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressColorEvents) return;
        var hex = AccentHex.Text.Trim();
        if (!IsValidHex(hex)) return;
        _pendingAccent = hex;
        AccentPreview.Background = TryParseBrush(hex);
    }

    private void BgHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressColorEvents) return;
        var hex = BgHex.Text.Trim();
        if (!IsValidHex(hex)) return;
        _pendingBg = hex;
        BgPreview.Background = TryParseBrush(hex);
    }

    private void CardHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressColorEvents) return;
        var hex = CardHex.Text.Trim();
        if (!IsValidHex(hex)) return;
        _pendingCard = hex;
        CardPreview.Background = TryParseBrush(hex);
    }

    private void TextHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressColorEvents) return;
        var hex = TextHex.Text.Trim();
        if (!IsValidHex(hex)) return;
        _pendingText = hex;
        TextPreview.Background = TryParseBrush(hex);
    }

    private void LoadBindings()
    {
        var saved = _settingsService.Settings.Hotkeys.Bindings;
        foreach (var (action, _, _) in KnownActions)
        {
            var binding = saved.FirstOrDefault(b => b.ActionName == action);
            _pending[action] = !string.IsNullOrWhiteSpace(binding?.Gesture)
                ? binding!.Gesture
                : DefaultGestures.GetValueOrDefault(action, string.Empty);
        }
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();
        _rowRefs.Clear();

        foreach (var (action, displayName, icon) in KnownActions)
        {
            RowsPanel.Children.Add(BuildRow(action, displayName, icon));
        }
    }

    private UIElement BuildRow(string action, string displayName, string icon)
    {
        var outerBorder = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(WpfColor.FromRgb(0x1C, 0x1C, 0x30)),
            Padding = new Thickness(14, 10, 14, 10),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 15,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        Grid.SetColumn(iconText, 0);

        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0xD8, 0xD8, 0xF0)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(nameText, 1);

        var gesture = _pending.GetValueOrDefault(action, string.Empty);

        var chipText = new TextBlock
        {
            Text = string.IsNullOrEmpty(gesture) ? "Not set" : gesture,
            FontSize = 12,
            FontFamily = new WpfFontFamily("Consolas"),
            Foreground = GestureColor(gesture),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };

        var chip = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(0x12, 0x12, 0x26)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 148,
            Child = chipText,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(chip, 2);

        var changeBtn = new System.Windows.Controls.Button
        {
            Content = "Change",
            Margin = new Thickness(4, 0, 0, 0),
            Style = (Style)FindResource("ActionBtn"),
        };
        Grid.SetColumn(changeBtn, 3);

        var capturedAction = action;
        changeBtn.Click += (_, _) =>
        {
            if (_listeningAction == capturedAction)
                StopListening();
            else
                StartListening(capturedAction);
        };

        var clearBtn = new System.Windows.Controls.Button
        {
            Content = "✕",
            ToolTip = "Clear binding",
            Margin = new Thickness(4, 0, 0, 0),
            Style = (Style)FindResource("ClearBtn"),
        };
        Grid.SetColumn(clearBtn, 4);

        clearBtn.Click += (_, _) =>
        {
            _pending[capturedAction] = string.Empty;
            if (_listeningAction == capturedAction) StopListening();
            UpdateChip(capturedAction, string.Empty);
        };

        grid.Children.Add(iconText);
        grid.Children.Add(nameText);
        grid.Children.Add(chip);
        grid.Children.Add(changeBtn);
        grid.Children.Add(clearBtn);

        outerBorder.Child = grid;
        _rowRefs[action] = (chip, chipText, changeBtn);
        return outerBorder;
    }

    private void StartListening(string action)
    {
        if (_listeningAction != null) StopListening();

        _listeningAction = action;

        if (!_rowRefs.TryGetValue(action, out var refs)) return;

        refs.Chip.Background = new SolidColorBrush(WpfColor.FromRgb(0x0E, 0x2E, 0x1A));
        refs.ChipText.Text = "⌨  Press keys...";
        refs.ChipText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x55, 0xDD, 0x88));
        refs.ChangeBtn.Content = "Cancel";
    }

    private void StopListening()
    {
        if (_listeningAction == null) return;
        var action = _listeningAction;
        _listeningAction = null;

        if (!_rowRefs.TryGetValue(action, out var refs)) return;
        refs.ChangeBtn.Content = "Change";
        UpdateChip(action, _pending.GetValueOrDefault(action, string.Empty));
    }

    private void UpdateChip(string action, string gesture)
    {
        if (!_rowRefs.TryGetValue(action, out var refs)) return;
        refs.Chip.Background = new SolidColorBrush(WpfColor.FromRgb(0x12, 0x12, 0x26));
        refs.ChipText.Text = string.IsNullOrEmpty(gesture) ? "Not set" : gesture;
        refs.ChipText.Foreground = GestureColor(gesture);
    }

    private static System.Windows.Media.Brush GestureColor(string gesture) =>
        string.IsNullOrEmpty(gesture)
            ? new SolidColorBrush(WpfColor.FromRgb(0x44, 0x44, 0x66))
            : new SolidColorBrush(WpfColor.FromRgb(0x88, 0xBB, 0xFF));

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_listeningAction == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            StopListening();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) parts.Add("Alt");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) parts.Add("Shift");
        parts.Add(KeyToGesturePart(key));

        var gesture = string.Join("+", parts);
        var captured = _listeningAction;
        _pending[captured] = gesture;
        StopListening();
        UpdateChip(captured, gesture);
        e.Handled = true;
    }

    private static string KeyToGesturePart(Key key) => key switch
    {
        Key.F1  => "F1",  Key.F2  => "F2",  Key.F3  => "F3",  Key.F4  => "F4",
        Key.F5  => "F5",  Key.F6  => "F6",  Key.F7  => "F7",  Key.F8  => "F8",
        Key.F9  => "F9",  Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
        Key.VolumeUp   => "VolumeUp",
        Key.VolumeDown => "VolumeDown",
        Key.VolumeMute => "VolumeMute",
        Key.OemTilde   => "`",
        Key.OemMinus   => "-",
        Key.Up    => "Up",
        Key.Down  => "Down",
        Key.Left  => "Left",
        Key.Right => "Right",
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        Key.NumPad0 => "0", Key.NumPad1 => "1", Key.NumPad2 => "2",
        Key.NumPad3 => "3", Key.NumPad4 => "4", Key.NumPad5 => "5",
        Key.NumPad6 => "6", Key.NumPad7 => "7", Key.NumPad8 => "8", Key.NumPad9 => "9",
        Key.Space       => "Space",
        Key.Enter       => "Enter",
        Key.Tab         => "Tab",
        Key.Back        => "Backspace",
        Key.Delete      => "Delete",
        Key.Home        => "Home",
        Key.End         => "End",
        Key.PageUp      => "PageUp",
        Key.PageDown    => "PageDown",
        Key.Insert      => "Insert",
        Key.PrintScreen => "PrintScreen",
        Key.Scroll      => "ScrollLock",
        Key.Pause       => "Pause",
        _ when key >= Key.A && key <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        _ => key.ToString(),
    };

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        // Reset whichever tab is active
        var isHotkeys = MainTabs.SelectedIndex == 0;

        if (isHotkeys)
        {
            foreach (var (action, _, _) in KnownActions)
                _pending[action] = DefaultGestures.GetValueOrDefault(action, string.Empty);
            BuildRows();
        }
        else
        {
            _pendingFontSize  = 14;
            _pendingOpacity   = 100;
            _pendingLargeText = false;
            _pendingLayout    = OverlayLayout.Vertical;
            _pendingAccent    = "#4ADE80";
            _pendingBg        = "#0F0F14";
            _pendingCard      = "#1E1F2A";
            _pendingText      = "#F0F0F5";

            _suppressColorEvents = true;
            FontSizeSlider.Value = _pendingFontSize;
            FontSizeLabel.Text   = _pendingFontSize.ToString();
            OpacitySlider.Value  = _pendingOpacity;
            OpacityLabel.Text    = "100%";
            LargeTextCheck.IsChecked   = false;
            LayoutVertical.IsChecked   = true;
            LayoutHorizontal.IsChecked = false;
            SetHexAndPreview(AccentHex, AccentPreview, _pendingAccent);
            SetHexAndPreview(BgHex,     BgPreview,     _pendingBg);
            SetHexAndPreview(CardHex,   CardPreview,   _pendingCard);
            SetHexAndPreview(TextHex,   TextPreview,   _pendingText);
            _suppressColorEvents = false;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        // ── Save hotkeys ──
        var bindings = _settingsService.Settings.Hotkeys.Bindings;
        bindings.Clear();

        foreach (var (action, _, _) in KnownActions)
        {
            var gesture = _pending.GetValueOrDefault(action, string.Empty);
            bindings.Add(new HotkeyBinding
            {
                ActionName = action,
                Gesture    = gesture,
                Enabled    = !string.IsNullOrEmpty(gesture),
            });
        }

        // ── Save appearance ──
        var ui = _settingsService.Settings.Ui;
        ui.FontSize               = _pendingFontSize;
        ui.Opacity                = Math.Clamp(_pendingOpacity / 100.0, 0.25, 1.0);
        ui.LargeTextMode          = _pendingLargeText;
        ui.Layout                 = _pendingLayout;
        ui.AccentColor            = _pendingAccent;
        ui.OverlayBackgroundColor = _pendingBg;
        ui.CardColor              = _pendingCard;
        ui.TextColor              = _pendingText;

        _settingsService.Save();
        _onSaved();
        Close();
    }
}
