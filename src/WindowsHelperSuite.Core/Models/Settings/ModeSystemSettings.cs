using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.Core.Models.Settings;

public class ModeSystemSettings
{
    public AppMode CurrentMode { get; set; } = AppMode.Hotkey;

    /// <summary>Gesture for the mode menu (V1: fixed; reserved for future customization).</summary>
    public string MenuHotkeyGesture { get; set; } = "Ctrl+F3";

    public bool ShowModeToast { get; set; } = true;

    public bool SpeakModeChange { get; set; } = false;

    public bool RememberLastMode { get; set; } = true;
}
