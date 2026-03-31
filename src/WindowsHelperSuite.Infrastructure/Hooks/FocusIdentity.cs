using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>Cheap focus key for caching UIA-heavy checks (foreground HWND + keyboard focus HWND).</summary>
public static class FocusIdentity
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool TryGet(out nint foregroundHwnd, out nint focusHwnd)
    {
        foregroundHwnd = nint.Zero;
        focusHwnd = nint.Zero;

        try
        {
            var fg = GetForegroundWindow();
            if (fg == nint.Zero)
            {
                return false;
            }

            var tid = GetWindowThreadProcessId(fg, out _);
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(tid, ref info))
            {
                return false;
            }

            foregroundHwnd = fg;
            focusHwnd = info.hwndFocus;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
