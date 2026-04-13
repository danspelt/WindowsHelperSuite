using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>Bring another process window to the foreground (Windows restricts naive <see cref="SetForegroundWindow"/>).</summary>
public static class Win32WindowActivation
{
    public const int SwShow = 5;
    public const int SwRestore = 9;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    /// <summary>Returns the thread id that owns the window.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(nint hWnd);

    /// <summary>
    /// Uses AttachThreadInput so <see cref="SetForegroundWindow"/> is allowed from a low-level keyboard hook thread.
    /// </summary>
    public static bool TryForceForegroundWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return false;

        var fg = GetForegroundWindow();
        if (fg == hWnd)
            return true;

        var targetThread = GetWindowThreadProcessId(hWnd, out _);
        var curThread = GetCurrentThreadId();

        var fgThread = fg == nint.Zero ? 0u : GetWindowThreadProcessId(fg, out _);

        var attachedFg = false;
        if (fg != nint.Zero && fgThread != 0 && fgThread != curThread)
            attachedFg = AttachThreadInput(curThread, fgThread, true);

        try
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SwRestore);
            else
                ShowWindow(hWnd, SwShow);

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            if (GetForegroundWindow() == hWnd)
                return true;
        }
        finally
        {
            if (attachedFg)
                AttachThreadInput(curThread, fgThread, false);
        }

        // Second attempt: attach to the target window's thread (helps some focus chains).
        var attachedTarget = false;
        if (targetThread != 0 && targetThread != curThread)
            attachedTarget = AttachThreadInput(curThread, targetThread, true);
        try
        {
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            return GetForegroundWindow() == hWnd;
        }
        finally
        {
            if (attachedTarget)
                AttachThreadInput(curThread, targetThread, false);
        }
    }
}
