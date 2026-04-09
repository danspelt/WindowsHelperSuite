namespace WindowsHelperSuite.Core.Models.Settings;

public enum SpeakMode
{
    WordsOnly,
    SentencesOnly,
    Both
}

/// <summary>
/// Which speech engines may run: Azure (online neural) and/or System.Speech (offline).
/// </summary>
public enum SpeechVoiceMode
{
    /// <summary>Try Azure first; on failure or no network, use local System.Speech.</summary>
    BestQualityOnlineWithOfflineBackup,

    /// <summary>Local voices only.</summary>
    OfflineOnly,

    /// <summary>Azure only; no local fallback.</summary>
    OnlineOnly
}

public class SpeechSettings
{
    public bool EnableSpeechOnSelection { get; set; } = true;

    /// <summary>Speak suggestions while moving the keyboard highlight (↑↓). Independent of selection speech.</summary>
    public bool EnableSpeechOnHighlight { get; set; } = true;

    /// <summary>Delay before speaking after highlight changes; reduces TTS spam when scrolling quickly (0–2000 ms).</summary>
    public int HighlightSpeechDebounceMs { get; set; } = 200;
    public bool OnlySpeakOnHeadset { get; set; } = true;
    public string PreferredDeviceName { get; set; } = string.Empty;

    /// <summary>Azure neural voice (e.g. en-US-AvaNeural). Empty uses the app default.</summary>
    public string VoiceName { get; set; } = string.Empty;

    /// <summary>Slightly slower out of the box for clearer feedback; range still -2..+2 from UI.</summary>
    public int SpeechRate { get; set; } = -1;
    public int SpeechVolume { get; set; } = 100;
    public SpeakMode SpeakMode { get; set; } = SpeakMode.Both;

    public SpeechVoiceMode VoiceMode { get; set; } = SpeechVoiceMode.BestQualityOnlineWithOfflineBackup;

    /// <summary>Azure Speech resource key; if empty, AZURE_SPEECH_KEY or SPEECH_KEY is used.</summary>
    public string AzureSpeechKey { get; set; } = string.Empty;

    /// <summary>Azure region (e.g. eastus); if empty, AZURE_SPEECH_REGION or SPEECH_REGION is used.</summary>
    public string AzureSpeechRegion { get; set; } = string.Empty;

    /// <summary>SSML prosody pitch for Azure (e.g. 0%, +2%, +1st).</summary>
    public string OnlinePitch { get; set; } = "+2%";

    /// <summary>SSML prosody volume for Azure: default, silent, soft, medium, loud, or relative like +6dB.</summary>
    public string OnlineVolumeProsody { get; set; } = "default";

    /// <summary>Azure neural speaking style (mstts:express-as), e.g. friendly, chat, customerservice. Empty disables.</summary>
    public string OnlineExpressAsStyle { get; set; } = "friendly";

    /// <summary>Installed System.Speech voice name; empty picks best en-US / en-CA match.</summary>
    public string OfflineVoiceName { get; set; } = string.Empty;
}
