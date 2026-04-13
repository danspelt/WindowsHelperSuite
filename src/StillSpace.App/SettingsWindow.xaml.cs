using System.Globalization;
using System.Windows;
using StillSpace.Services;

namespace StillSpace;

public partial class SettingsWindow : Window
{
    public StillSpaceSettings Result { get; private set; } = new();

    public SettingsWindow(StillSpaceSettings initial)
    {
        InitializeComponent();
        ApiKeyBox.Text = initial.OpenAiApiKey;
        PreferredNameBox.Text = initial.PreferredName;
        SttLangBox.Text = initial.SttLang;
        AiDictationHintsBox.IsChecked = initial.AiDictationNextWordHints;
        HintModelBox.Text = initial.OpenAiHintModel;
        PauseMsBox.Text = initial.PauseBeforeReplyMs.ToString(CultureInfo.InvariantCulture);
        ChatModelBox.Text = initial.OpenAiChatModel;
        TtsModelBox.Text = initial.OpenAiTtsModel;
        TtsVoiceBox.Text = initial.OpenAiTtsVoice;
        RealtimeModelBox.Text = initial.OpenAiRealtimeModel;
        RealtimeVoiceBox.Text = string.IsNullOrWhiteSpace(initial.OpenAiRealtimeVoice)
            ? "marin"
            : initial.OpenAiRealtimeVoice;
        ResponsivenessCombo.SelectedIndex = initial.RealtimeResponsiveness switch
        {
            RealtimeResponsivenessPreset.Fast => 0,
            RealtimeResponsivenessPreset.Patient => 2,
            _ => 1
        };
        ShowRealtimeDiagBox.IsChecked = initial.ShowRealtimeVoiceDiagnostics;
        LogTimingsBox.IsChecked = initial.LogRealtimeTurnTimings;
        AutoReadAloudBox.IsChecked = initial.AutoReadAloud;
        PreferOpenAiTtsBox.IsChecked = initial.PreferOpenAiTts;
        HeadsetOnlyBox.IsChecked = initial.HeadsetOnlyMode;
        HeadsetMatchBox.Text = initial.HeadsetNameMatch;
        OutputIdBox.Text = initial.PreferredOutputDeviceId;
        InputIdBox.Text = initial.PreferredInputDeviceId;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new StillSpaceSettings
        {
            OpenAiApiKey = ApiKeyBox.Text.Trim(),
            PreferredName = PreferredNameBox.Text.Trim(),
            SttLang = string.IsNullOrWhiteSpace(SttLangBox.Text) ? "en-US" : SttLangBox.Text.Trim(),
            AiDictationNextWordHints = AiDictationHintsBox.IsChecked != false,
            OpenAiHintModel = HintModelBox.Text.Trim(),
            PauseBeforeReplyMs = int.TryParse(PauseMsBox.Text.Trim(), out var p) ? Math.Clamp(p, 0, 60_000) : 400,
            OpenAiChatModel = ChatModelBox.Text.Trim(),
            OpenAiTtsModel = TtsModelBox.Text.Trim(),
            OpenAiTtsVoice = string.IsNullOrWhiteSpace(TtsVoiceBox.Text) ? "alloy" : TtsVoiceBox.Text.Trim(),
            OpenAiRealtimeModel = RealtimeModelBox.Text.Trim(),
            OpenAiRealtimeVoice = string.IsNullOrWhiteSpace(RealtimeVoiceBox.Text)
                ? "marin"
                : RealtimeVoiceBox.Text.Trim(),
            RealtimeResponsiveness = ResponsivenessCombo.SelectedIndex switch
            {
                0 => RealtimeResponsivenessPreset.Fast,
                2 => RealtimeResponsivenessPreset.Patient,
                _ => RealtimeResponsivenessPreset.Balanced
            },
            ShowRealtimeVoiceDiagnostics = ShowRealtimeDiagBox.IsChecked == true,
            LogRealtimeTurnTimings = LogTimingsBox.IsChecked == true,
            AutoReadAloud = AutoReadAloudBox.IsChecked == true,
            PreferOpenAiTts = PreferOpenAiTtsBox.IsChecked == true,
            HeadsetOnlyMode = HeadsetOnlyBox.IsChecked == true,
            HeadsetNameMatch = string.IsNullOrWhiteSpace(HeadsetMatchBox.Text) ? "OpenRun" : HeadsetMatchBox.Text.Trim(),
            PreferredOutputDeviceId = OutputIdBox.Text.Trim(),
            PreferredInputDeviceId = InputIdBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
