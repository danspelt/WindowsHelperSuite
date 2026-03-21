using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>
/// Helper class for injecting text into the active window using Win32 API
/// </summary>
public static class Win32TextInjection
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Sends text to the currently focused window
    /// </summary>
    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Use SendKeys for simple text injection
        // This works reliably for most applications
        try
        {
            // Ensure we're sending to the foreground window
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return;

            // Temporarily attach to the foreground thread to ensure focus
            uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
            uint currentThreadId = GetCurrentThreadId();

            if (foregroundThreadId != currentThreadId)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            // Send the text using SendKeys
            // Escape special characters for SendKeys
            string escapedText = EscapeForSendKeys(text);
            SendKeys.SendWait(escapedText);

            if (foregroundThreadId != currentThreadId)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            System.Diagnostics.Debug.WriteLine($"Text injection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends backspace key to delete characters
    /// </summary>
    public static void SendBackspace(int count)
    {
        try
        {
            for (int i = 0; i < count; i++)
            {
                SendKeys.SendWait("{BACKSPACE}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Backspace injection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Escapes special characters for SendKeys
    /// </summary>
    private static string EscapeForSendKeys(string text)
    {
        var result = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '+':
                case '^':
                case '%':
                case '~':
                case '(':
                case ')':
                case '{':
                case '}':
                case '[':
                case ']':
                    result.Append($"{{{c}}}");
                    break;
                case ' ':
                    result.Append(" ");
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }
        return result.ToString();
    }
}
