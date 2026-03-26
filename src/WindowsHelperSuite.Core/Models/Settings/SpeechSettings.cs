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
    public bool OnlySpeakOnHeadset { get; set; } = true;
    public string PreferredDeviceName { get; set; } = string.Empty;

    /// <summary>Azure neural voice (e.g. en-US-JennyNeural). Empty uses the app default.</summary>
    public string VoiceName { get; set; } = string.Empty;

    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
    public SpeakMode SpeakMode { get; set; } = SpeakMode.Both;

    public SpeechVoiceMode VoiceMode { get; set; } = SpeechVoiceMode.BestQualityOnlineWithOfflineBackup;

    /// <summary>Azure Speech resource key; if empty, AZURE_SPEECH_KEY or SPEECH_KEY is used.</summary>
    public string AzureSpeechKey { get; set; } = string.Empty;

    /// <summary>Azure region (e.g. eastus); if empty, AZURE_SPEECH_REGION or SPEECH_REGION is used.</summary>
    public string AzureSpeechRegion { get; set; } = string.Empty;

    /// <summary>SSML prosody pitch for Azure (e.g. 0%, +2st).</summary>
    public string OnlinePitch { get; set; } = "0%";

    /// <summary>SSML prosody volume for Azure: default, silent, soft, medium, loud, or relative like +6dB.</summary>
    public string OnlineVolumeProsody { get; set; } = "default";

    /// <summary>Installed System.Speech voice name; empty picks best en-US / en-CA match.</summary>
    public string OfflineVoiceName { get; set; } = string.Empty;
}
