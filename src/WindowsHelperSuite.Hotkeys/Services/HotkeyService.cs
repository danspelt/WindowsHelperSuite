#pragma warning disable CS0067

using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Hotkeys.Services;

public class HotkeyService : IHotkeyService
{
    public event EventHandler<string>? HotkeyPressed;

    public void RegisterHotkey(string actionName, string gesture) { }
    public void UnregisterHotkey(string actionName) { }
}

#pragma warning restore CS0067
