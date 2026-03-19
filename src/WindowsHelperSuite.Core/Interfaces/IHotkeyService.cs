namespace WindowsHelperSuite.Core.Interfaces;

public interface IHotkeyService
{
    void RegisterHotkey(string actionName, string gesture);
    void UnregisterHotkey(string actionName);
    event EventHandler<string>? HotkeyPressed;
}
