namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>
/// Receives physical key notifications while the mode menu is open.
/// Return true to swallow the key at the low-level hook (focus may still be in another app).
/// </summary>
public interface IModeMenuKeySink
{
    bool TryConsumeKey(int virtualKey, bool ctrl, bool shift, bool alt);
}
