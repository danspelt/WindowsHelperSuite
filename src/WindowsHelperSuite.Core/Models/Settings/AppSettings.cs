namespace WindowsHelperSuite.Core.Models.Settings;

public class AppSettings
{
    public AiWriterSettings Ai { get; set; } = new();
    public WriterSettings Writer { get; set; } = new();
    public SpeechSettings Speech { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public ModeSystemSettings ModeSystem { get; set; } = new();

    public QuickTextSettings QuickText { get; set; } = new();

    public MongoVocabularySettings MongoVocabulary { get; set; } = new();

    public VoiceBridgeSettings VoiceBridge { get; set; } = new();
}
