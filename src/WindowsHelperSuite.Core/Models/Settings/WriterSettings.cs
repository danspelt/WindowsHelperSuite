namespace WindowsHelperSuite.Core.Models.Settings;

public class WriterSettings
{
    public bool AutoShowSuggestions { get; set; } = true;
    public string ManualTriggerKey { get; set; } = "`";
    public int MaxSuggestions { get; set; } = 9;
    public int DebounceTimeMs { get; set; } = 150;
    public bool FollowCaret { get; set; } = true;

    /// <summary>When true, Writer is active on startup without pressing Wake Writer.</summary>
    public bool StartAwake { get; set; }

    public string DockPosition { get; set; } = "BottomCenter";

    /// <summary>Capitalize sentence starts after . ! ? and at beginning of text (insert, space, paste).</summary>
    public bool AutoCapitalizeSentences { get; set; } = true;

    /// <summary>When auto-cap is on, normalize a lone typed "i" to "I".</summary>
    public bool CapitalizeSingleLetterI { get; set; } = true;

    /// <summary>
    /// When true, boost phrase suggestions using lightweight substring checks on the foreground window title
    /// (e.g. web mail). Off by default for privacy; titles are not logged.
    /// </summary>
    public bool UseWindowTitleForPhraseHints { get; set; }
}
