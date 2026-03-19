namespace WindowsHelperSuite.Core.Models.Settings;

public class WriterSettings
{
    public bool AutoShowSuggestions { get; set; } = true;
    public string ManualTriggerKey { get; set; } = "`";
    public int MaxSuggestions { get; set; } = 9;
    public int DebounceTimeMs { get; set; } = 150;
    public bool FollowCaret { get; set; } = true;
    public string DockPosition { get; set; } = "BottomCenter";
}
