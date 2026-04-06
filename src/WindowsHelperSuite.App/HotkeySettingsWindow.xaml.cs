using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Infrastructure.Services;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using HotkeyBinding = WindowsHelperSuite.Core.Models.KeyBinding;

namespace WindowsHelperSuite.App;

public partial class HotkeySettingsWindow : Window
{
    public const int TabGeneral = 0;
    public const int TabHotkeys = 1;
    public const int TabSpeech = 2;
    public const int TabWriter = 3;
    public const int TabWordsPhrases = 4;

    private readonly ISettingsService _settingsService;
    private readonly Action _onSaved;
    private readonly int? _initialTabIndex;

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
        ("OpenModeMenu",               "Open Quick Menu", "☰"),
        ("OpenSettings",               "Open Settings",    "⚙"),
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
        ["OpenModeMenu"]               = "Ctrl+Shift+F3",
        ["OpenSettings"]               = "Ctrl+F3",
    };

    private readonly Dictionary<string, string> _pending = new();
    private string? _listeningAction;

    private readonly Dictionary<string, (Border Chip, TextBlock ChipText, System.Windows.Controls.Button ChangeBtn)> _rowRefs = new();

    private QuickTextSettings _pendingQuickText = new();
    private SpeechSettings _pendingSpeech = new();
    private WriterSettings _pendingWriter = new();
    private bool _speechUiWired;
    private bool _writerUiWired;

    private static readonly JsonSerializerOptions CloneJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public HotkeySettingsWindow(ISettingsService settingsService, Action onSaved, int? initialTabIndex = null)
    {
        _settingsService = settingsService;
        _onSaved = onSaved;
        _initialTabIndex = initialTabIndex;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyNativeDarkFrame();
        _pendingQuickText = QuickTextSettingsService.Clone(_settingsService.Settings.QuickText);
        _pendingSpeech = DeepClone(_settingsService.Settings.Speech);
        _pendingWriter = DeepClone(_settingsService.Settings.Writer);
        LoadBindings();
        LoadAppearance();
        Loaded += (_, _) =>
        {
            BuildRows();
            BuildAppearanceTab();
            BuildSpeechTab();
            BuildWriterTab();
            WordsPhrasesPanel.Attach(_pendingQuickText);
            if (_initialTabIndex is int tab)
            {
                MainTabs.SelectedIndex = Math.Clamp(tab, 0, MainTabs.Items.Count - 1);
            }
        };
    }

    public void NavigateToTab(int index)
    {
        MainTabs.SelectedIndex = Math.Clamp(index, 0, MainTabs.Items.Count - 1);
        Activate();
    }

    private static T DeepClone<T>(T value)
        where T : class
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneJson), CloneJson)!;
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

    private void BuildSpeechTab()
    {
        if (!_speechUiWired)
        {
            SpeakModeCombo.Items.Clear();
            foreach (SpeakMode m in Enum.GetValues(typeof(SpeakMode)))
            {
                SpeakModeCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = m.ToString(), Tag = m });
            }

            VoiceModeCombo.Items.Clear();
            foreach (SpeechVoiceMode m in Enum.GetValues(typeof(SpeechVoiceMode)))
            {
                VoiceModeCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = DescribeVoiceMode(m), Tag = m });
            }

            SpeechRateSlider.ValueChanged += (_, _) =>
            {
                _pendingSpeech.SpeechRate = (int)Math.Round(SpeechRateSlider.Value);
                SpeechRateLabel.Text = _pendingSpeech.SpeechRate.ToString();
            };
            SpeechVolumeSlider.ValueChanged += (_, _) =>
            {
                _pendingSpeech.SpeechVolume = (int)Math.Round(SpeechVolumeSlider.Value);
                SpeechVolumeLabel.Text = _pendingSpeech.SpeechVolume.ToString();
            };
            SpeakModeCombo.SelectionChanged += (_, _) =>
            {
                if (SpeakModeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: SpeakMode sm })
                {
                    _pendingSpeech.SpeakMode = sm;
                }
            };
            VoiceModeCombo.SelectionChanged += (_, _) =>
            {
                if (VoiceModeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: SpeechVoiceMode vm })
                {
                    _pendingSpeech.VoiceMode = vm;
                }
            };
            SpeechVoiceNameBox.TextChanged += (_, _) => _pendingSpeech.VoiceName = SpeechVoiceNameBox.Text.Trim();
            SpeechEnableSelection.Checked += (_, _) => _pendingSpeech.EnableSpeechOnSelection = true;
            SpeechEnableSelection.Unchecked += (_, _) => _pendingSpeech.EnableSpeechOnSelection = false;
            SpeechHeadsetOnly.Checked += (_, _) => _pendingSpeech.OnlySpeakOnHeadset = true;
            SpeechHeadsetOnly.Unchecked += (_, _) => _pendingSpeech.OnlySpeakOnHeadset = false;
            _speechUiWired = true;
        }

        SpeechEnableSelection.IsChecked = _pendingSpeech.EnableSpeechOnSelection;
        SpeechHeadsetOnly.IsChecked = _pendingSpeech.OnlySpeakOnHeadset;
        SpeechRateSlider.Value = Math.Clamp(_pendingSpeech.SpeechRate, -2, 2);
        SpeechRateLabel.Text = _pendingSpeech.SpeechRate.ToString();
        SpeechVolumeSlider.Value = Math.Clamp(_pendingSpeech.SpeechVolume, 0, 100);
        SpeechVolumeLabel.Text = _pendingSpeech.SpeechVolume.ToString();
        SpeechVoiceNameBox.Text = _pendingSpeech.VoiceName;
        SelectComboByTag(SpeakModeCombo, (object)_pendingSpeech.SpeakMode);
        SelectComboByTag(VoiceModeCombo, (object)_pendingSpeech.VoiceMode);
    }

    private void BuildWriterTab()
    {
        if (!_writerUiWired)
        {
            WriterAutoShow.Checked += (_, _) => _pendingWriter.AutoShowSuggestions = true;
            WriterAutoShow.Unchecked += (_, _) => _pendingWriter.AutoShowSuggestions = false;
            WriterFollowCaret.Checked += (_, _) => _pendingWriter.FollowCaret = true;
            WriterFollowCaret.Unchecked += (_, _) => _pendingWriter.FollowCaret = false;
            WriterManualKeyBox.TextChanged += (_, _) => _pendingWriter.ManualTriggerKey = WriterManualKeyBox.Text.Trim();
            WriterDockBox.TextChanged += (_, _) => _pendingWriter.DockPosition = WriterDockBox.Text.Trim();
            WriterMaxSugSlider.ValueChanged += (_, _) =>
            {
                _pendingWriter.MaxSuggestions = (int)Math.Round(WriterMaxSugSlider.Value);
                WriterMaxSugLabel.Text = _pendingWriter.MaxSuggestions.ToString();
            };
            WriterDebounceSlider.ValueChanged += (_, _) =>
            {
                _pendingWriter.DebounceTimeMs = (int)Math.Round(WriterDebounceSlider.Value);
                WriterDebounceLabel.Text = _pendingWriter.DebounceTimeMs.ToString();
            };
            WriterAutoCap.Checked += (_, _) => _pendingWriter.AutoCapitalizeSentences = true;
            WriterAutoCap.Unchecked += (_, _) => _pendingWriter.AutoCapitalizeSentences = false;
            WriterCapI.Checked += (_, _) => _pendingWriter.CapitalizeSingleLetterI = true;
            WriterCapI.Unchecked += (_, _) => _pendingWriter.CapitalizeSingleLetterI = false;
            _writerUiWired = true;
        }

        WriterAutoShow.IsChecked = _pendingWriter.AutoShowSuggestions;
        WriterFollowCaret.IsChecked = _pendingWriter.FollowCaret;
        WriterManualKeyBox.Text = _pendingWriter.ManualTriggerKey;
        WriterDockBox.Text = _pendingWriter.DockPosition;
        WriterMaxSugSlider.Value = Math.Clamp(_pendingWriter.MaxSuggestions, 3, 15);
        WriterMaxSugLabel.Text = _pendingWriter.MaxSuggestions.ToString();
        WriterDebounceSlider.Value = Math.Clamp(_pendingWriter.DebounceTimeMs, 50, 500);
        WriterDebounceLabel.Text = _pendingWriter.DebounceTimeMs.ToString();
        WriterAutoCap.IsChecked = _pendingWriter.AutoCapitalizeSentences;
        WriterCapI.IsChecked = _pendingWriter.CapitalizeSingleLetterI;
    }

    private static string DescribeVoiceMode(SpeechVoiceMode m) => m switch
    {
        SpeechVoiceMode.BestQualityOnlineWithOfflineBackup => "Online + offline backup",
        SpeechVoiceMode.OfflineOnly => "Offline only",
        SpeechVoiceMode.OnlineOnly => "Online only",
        _ => m.ToString(),
    };

    private static void SelectComboByTag(System.Windows.Controls.ComboBox box, object value)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in box.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (item.Tag?.Equals(value) == true)
            {
                box.SelectedItem = item;
                return;
            }
        }
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
        switch (MainTabs.SelectedIndex)
        {
            case 0:
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
                break;
            case 1:
                foreach (var (action, _, _) in KnownActions)
                {
                    _pending[action] = DefaultGestures.GetValueOrDefault(action, string.Empty);
                }

                BuildRows();
                break;
            case 2:
                _pendingSpeech = new SpeechSettings();
                BuildSpeechTab();
                break;
            case 3:
                _pendingWriter = new WriterSettings();
                BuildWriterTab();
                break;
            case 4:
                QuickTextSettingsService.ResetToFactoryDefaults(_pendingQuickText);
                WordsPhrasesPanel.Attach(_pendingQuickText);
                break;
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

        _settingsService.Settings.QuickText = QuickTextSettingsService.Clone(_pendingQuickText);
        _settingsService.Settings.Speech = DeepClone(_pendingSpeech);
        _settingsService.Settings.Writer = DeepClone(_pendingWriter);

        _settingsService.Save();
        _onSaved();
        Close();
    }

    /// <summary>Paints the system frame to match the dark UI so the default light DWM ring disappears.</summary>
    private void ApplyNativeDarkFrame()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero)
        {
            return;
        }

        const int DwmwaUseImmersiveDarkMode = 20;
        const int DwmwaBorderColor = 34;
        const int DwmwaCaptionColor = 35;
        const int DwmwaCaptionText = 36;

        int one = 1;
        _ = DwmSetWindowAttribute(h, DwmwaUseImmersiveDarkMode, ref one, sizeof(int));

        int borderRgb = ColorRefFromRgb(0x0F, 0x0F, 0x1A);
        _ = DwmSetWindowAttribute(h, DwmwaBorderColor, ref borderRgb, sizeof(int));

        int captionRgb = ColorRefFromRgb(0x12, 0x12, 0x1C);
        _ = DwmSetWindowAttribute(h, DwmwaCaptionColor, ref captionRgb, sizeof(int));

        int textRgb = ColorRefFromRgb(0xFA, 0xFA, 0xFF);
        _ = DwmSetWindowAttribute(h, DwmwaCaptionText, ref textRgb, sizeof(int));
    }

    private static int ColorRefFromRgb(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
