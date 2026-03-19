namespace WindowsHelperSuite.Core.Models.Settings;

public class SpeechSettings
{
    public bool EnableSpeechOnSelection { get; set; } = true;
    public bool OnlySpeakOnHeadset { get; set; } = true;
    public string PreferredDeviceName { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
}
