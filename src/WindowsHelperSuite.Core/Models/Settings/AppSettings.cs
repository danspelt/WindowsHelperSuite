namespace WindowsHelperSuite.Core.Models.Settings;

public class AppSettings
{
    public WriterSettings Writer { get; set; } = new();
    public SpeechSettings Speech { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public ModeSystemSettings ModeSystem { get; set; } = new();
}
