using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>
/// Injects text into the focused window via clipboard paste or Unicode keystrokes.
/// </summary>
public static class Win32TextInjection
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // INPUT struct must match native size (40 bytes on 64-bit).
    // The union must be 32 bytes (size of MOUSEINPUT, the largest member).
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;

    /// <summary>
    /// Sends text to the currently focused window via Unicode keystrokes only.
    /// Does not use the clipboard (clipboard-based paste left injected text behind when the clipboard was empty).
    /// </summary>
    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        SendUnicodeChars(text);
    }

    /// <summary>
    /// Sends backspace keys to delete characters.
    /// </summary>
    public static void SendBackspace(int count)
    {
        for (int i = 0; i < count; i++)
        {
            SendKeyPress(VK_BACK);
            Thread.Sleep(10);
        }
    }

    private static void SendUnicodeChars(string text)
    {
        foreach (var ch in text)
        {
            var inputs = new INPUT[2];
            inputs[0] = MakeUnicodeInput(ch, 0);
            inputs[1] = MakeUnicodeInput(ch, KEYEVENTF_KEYUP);
            var sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            if (sent != 2)
                System.Diagnostics.Debug.WriteLine($"SendInput returned {sent} for char '{ch}'");
        }
    }

    private static void SendKeyPress(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0] = MakeKeyInput(vk, 0);
        inputs[1] = MakeKeyInput(vk, KEYEVENTF_KEYUP);
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = flags }
            }
        };
    }

    private static INPUT MakeUnicodeInput(char ch, uint extraFlags)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)ch,
                    dwFlags = KEYEVENTF_UNICODE | extraFlags
                }
            }
        };
    }
}
