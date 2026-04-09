using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>Monitor work area for a screen point (multi-monitor safe).</summary>
public static class Win32Screen
{
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Returns false if API fails; caller should fall back to primary work area.</summary>
    public static bool TryGetWorkAreaForPoint(int screenX, int screenY, out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        var pt = new POINT { X = screenX, Y = screenY };
        var h = MonitorFromPoint(pt, MonitorDefaultToNearest);
        if (h == IntPtr.Zero)
        {
            return false;
        }

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(h, ref mi))
        {
            return false;
        }

        left = mi.rcWork.Left;
        top = mi.rcWork.Top;
        right = mi.rcWork.Right;
        bottom = mi.rcWork.Bottom;
        return true;
    }
}
