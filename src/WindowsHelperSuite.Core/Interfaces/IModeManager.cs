using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IModeManager
{
    AppMode CurrentMode { get; }

    /// <summary>Apply persisted mode on startup (no toast unless configured).</summary>
    void Initialize();

    ModeChangeResult SwitchMode(AppMode newMode);

    event EventHandler<AppMode>? ModeChanged;
}
