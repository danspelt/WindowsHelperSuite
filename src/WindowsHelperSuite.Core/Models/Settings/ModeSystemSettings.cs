using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.Core.Models.Settings;

public class ModeSystemSettings
{
    public AppMode CurrentMode { get; set; } = AppMode.Hotkey;

    public bool RememberLastMode { get; set; } = true;

    /// <summary>Gesture for <c>OpenModeMenu</c> when not overridden in hotkey bindings.</summary>
    public string MenuHotkeyGesture { get; set; } = "Ctrl+F3";
}
