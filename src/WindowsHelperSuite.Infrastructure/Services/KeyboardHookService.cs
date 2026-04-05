using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Infrastructure.Services;

public class KeyboardHookService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private Win32KeyboardHook.LowLevelKeyboardProc? _hookCallback;
    private readonly Dictionary<string, (uint keyCode, bool ctrl, bool alt, bool shift, bool consumeKeys)> _registeredHotkeys = [];
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HashSet<uint> _suppressedKeys = []; // Track keys whose key-UP should also be eaten
    private readonly ILoggingService _loggingService;
    private readonly object _hookThreadLock = new();
    private Thread? _pumpThread;
    private uint _pumpNativeThreadId;
    private readonly ManualResetEventSlim _hookInstallDone = new(false);

    public event EventHandler<string>? HotkeyPressed;
    public event EventHandler<KeyEventArgs>? KeyPressed;

    public KeyboardHookService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void StartHook()
    {
        lock (_hookThreadLock)
        {
            if (_pumpThread is { IsAlive: true })
            {
                return;
            }

            _hookInstallDone.Reset();
            _pumpThread = new Thread(HookPumpProc)
            {
                IsBackground = true,
                Name = "WindowsHelperSuite.KeyboardHook"
            };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            if (!_hookInstallDone.Wait(TimeSpan.FromSeconds(10)))
            {
                _loggingService.Warning("Keyboard hook thread did not finish installing within 10 seconds.");
            }
        }
    }

    public void StopHook()
    {
        lock (_hookThreadLock)
        {
            if (_pumpThread is not { IsAlive: true })
            {
                _hookId = IntPtr.Zero;
                _hookCallback = null;
                _pumpThread = null;
                _pumpNativeThreadId = 0;
                return;
            }

            if (_pumpNativeThreadId != 0)
            {
                Win32KeyboardHook.PostThreadMessage(_pumpNativeThreadId, Win32KeyboardHook.WM_QUIT, 0, 0);
            }

            if (!_pumpThread.Join(TimeSpan.FromSeconds(5)))
            {
                _loggingService.Warning("Keyboard hook thread did not exit cleanly.");
            }

            _pumpThread = null;
            _pumpNativeThreadId = 0;
            _hookId = IntPtr.Zero;
            _hookCallback = null;
        }
    }

    /// <summary>
    /// WH_KEYBOARD_LL must not run on the WPF UI thread: blocking there during key processing freezes the
    /// message pump, trips the low-level hook timeout, and crashes while typing.
    /// </summary>
    private void HookPumpProc()
    {
        _pumpNativeThreadId = Win32KeyboardHook.GetCurrentThreadId();

        try
        {
            _hookCallback = HookCallback;
            _hookId = SetHook(_hookCallback);
            if (_hookId == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                _loggingService.Warning(
                    $"Keyboard hook failed to install (Win32 error {err}). Writer and global hotkeys are disabled.");
                _hookCallback = null;
            }
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Keyboard hook failed: {ex.Message}");
            _hookCallback = null;
            _hookId = IntPtr.Zero;
        }
        finally
        {
            _hookInstallDone.Set();
        }

        Win32KeyboardHook.MSG msg;
        int gm;
        while ((gm = Win32KeyboardHook.GetMessage(out msg, 0, 0, 0)) != 0)
        {
            if (gm == -1)
            {
                break;
            }

            Win32KeyboardHook.TranslateMessage(ref msg);
            Win32KeyboardHook.DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            Win32KeyboardHook.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _hookCallback = null;
    }

    public void RegisterHotkey(string actionName, string gesture, bool consumeMatchingKeys = false)
    {
        var (keyCode, ctrl, alt, shift) = ParseGesture(gesture);
        _registeredHotkeys[actionName] = (keyCode, ctrl, alt, shift, consumeMatchingKeys);
    }

    public void UnregisterHotkey(string actionName)
    {
        _registeredHotkeys.Remove(actionName);
    }

    private static IntPtr SetHook(Win32KeyboardHook.LowLevelKeyboardProc proc)
    {
        // Do not use Process.MainModule — it throws on some hosts (permissions, single-file, tooling).
        var hMod = Win32KeyboardHook.GetModuleHandle(null);
        return Win32KeyboardHook.SetWindowsHookEx(
            Win32KeyboardHook.WH_KEYBOARD_LL,
            proc,
            hMod,
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<Win32KeyboardHook.KBDLLHOOKSTRUCT>(lParam);
                var vkCode = hookStruct.vkCode;
                var isKeyDown = wParam == (IntPtr)Win32KeyboardHook.WM_KEYDOWN ||
                               wParam == (IntPtr)Win32KeyboardHook.WM_SYSKEYDOWN;
                var isKeyUp = wParam == (IntPtr)Win32KeyboardHook.WM_KEYUP ||
                             wParam == (IntPtr)Win32KeyboardHook.WM_SYSKEYUP;

                // Skip keystrokes injected by our own SendInput (clipboard paste, backspace)
                if ((hookStruct.flags & Win32KeyboardHook.LLKHF_INJECTED) != 0)
                {
                    return Win32KeyboardHook.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (isKeyDown)
                {
                    _pressedKeys.Add(vkCode);
                    if (CheckHotkeys())
                    {
                        _suppressedKeys.Add(vkCode);
                        return (IntPtr)1;
                    }

                    var ctrlPressed = _pressedKeys.Contains(0x11) || _pressedKeys.Contains(0xA2) || _pressedKeys.Contains(0xA3);
                    var altPressed = _pressedKeys.Contains(0x12) || _pressedKeys.Contains(0xA4) || _pressedKeys.Contains(0xA5);
                    var shiftPressed = _pressedKeys.Contains(0x10) || _pressedKeys.Contains(0xA0) || _pressedKeys.Contains(0xA1);

                    var args = new KeyEventArgs((int)vkCode, shiftPressed, ctrlPressed, altPressed, Control.IsKeyLocked(Keys.CapsLock));
                    KeyPressed?.Invoke(this, args);

                    if (args.Handled)
                    {
                        _suppressedKeys.Add(vkCode); // Also suppress the matching key-UP
                        return (IntPtr)1;
                    }
                }
                else if (isKeyUp)
                {
                    _pressedKeys.Remove(vkCode);

                    // Eat the key-UP for any key whose key-DOWN we suppressed
                    if (_suppressedKeys.Remove(vkCode))
                    {
                        return (IntPtr)1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyboardHook: Exception in HookCallback: {ex}");
        }

        return Win32KeyboardHook.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>Returns true if the key event should be swallowed (consumeMatchingKeys hotkeys).</summary>
    private bool CheckHotkeys()
    {
        var suppress = false;
        foreach (var (actionName, (keyCode, ctrl, alt, shift, eat)) in _registeredHotkeys)
        {
            if (!_pressedKeys.Contains(keyCode))
            {
                continue;
            }

            var ctrlPressed = _pressedKeys.Contains(0x11) || _pressedKeys.Contains(0xA2) || _pressedKeys.Contains(0xA3);
            var altPressed = _pressedKeys.Contains(0x12) || _pressedKeys.Contains(0xA4) || _pressedKeys.Contains(0xA5);
            var shiftPressed = _pressedKeys.Contains(0x10) || _pressedKeys.Contains(0xA0) || _pressedKeys.Contains(0xA1);

            if (ctrl != ctrlPressed || alt != altPressed || shift != shiftPressed)
            {
                continue;
            }

            HotkeyPressed?.Invoke(this, actionName);
            if (eat)
            {
                suppress = true;
            }
        }

        return suppress;
    }

    private static (uint keyCode, bool ctrl, bool alt, bool shift) ParseGesture(string gesture)
    {
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ctrl = false;
        var alt = false;
        var shift = false;
        uint keyCode = 0;

        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    ctrl = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                case "SHIFT":
                    shift = true;
                    break;
                default:
                    keyCode = KeyCodeFromName(upper);
                    break;
            }
        }

        return (keyCode, ctrl, alt, shift);
    }

    private static uint KeyCodeFromName(string name)
    {
        if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
        {
            return char.ToUpperInvariant(name[0]);
        }

        return name switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "VOLUMEUP" => 0xAF,
            "VOLUMEDOWN" => 0xAE,
            "VOLUMEMUTE" => 0xAD,
            "`" => 0xC0,
            "GRAVE" => 0xC0,
            "1" => 0x31,
            "2" => 0x32,
            "3" => 0x33,
            "4" => 0x34,
            "5" => 0x35,
            "6" => 0x36,
            "7" => 0x37,
            "8" => 0x38,
            "9" => 0x39,
            "0" => 0x30,
            "MINUS" => 0xBD,
            "-" => 0xBD,
            "EQUALS" => 0xBB,
            "=" => 0xBB,
            "UP" => 0x26,
            "DOWN" => 0x28,
            _ => 0
        };
    }

    public void Dispose()
    {
        StopHook();
        _hookInstallDone.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class KeyEventArgs : EventArgs
{
    public int KeyCode { get; }
    public bool Shift { get; }
    public bool Ctrl { get; }
    public bool Alt { get; }
    public bool CapsLock { get; }
    public bool Handled { get; set; }

    public KeyEventArgs(int keyCode, bool shift, bool ctrl, bool alt, bool capsLock)
    {
        KeyCode = keyCode;
        Shift = shift;
        Ctrl = ctrl;
        Alt = alt;
        CapsLock = capsLock;
        Handled = false;
    }
}
