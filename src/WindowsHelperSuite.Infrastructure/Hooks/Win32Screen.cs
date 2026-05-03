using System.Runtime.InteropServices;
using System.Linq;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>Monitor work area for a screen point (multi-monitor safe).</summary>
public static class Win32Screen
{
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

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

    /// <summary>Gets work area for the next monitor (clockwise) from the specified point.</summary>
    public static bool TryGetNextScreenWorkArea(int screenX, int screenY, out int left, out int top, out int right, out int bottom)
    {
        var monitors = new List<MonitorInfo>();
        
        // Enumerate all monitors
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
            (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    monitors.Add(new MonitorInfo
                    {
                        Handle = hMonitor,
                        WorkArea = mi.rcWork,
                        MonitorArea = mi.rcMonitor,
                        IsPrimary = (mi.dwFlags & 1) != 0 // MONITORINFOF_PRIMARY = 1
                    });
                }
                return true;
            }, IntPtr.Zero);

        if (monitors.Count <= 1)
        {
            // Single monitor setup - fall back to current screen
            return TryGetWorkAreaForPoint(screenX, screenY, out left, out top, out right, out bottom);
        }

        // Find current monitor
        var currentMonitor = monitors.FirstOrDefault(m => 
            screenX >= m.WorkArea.Left && screenX < m.WorkArea.Right &&
            screenY >= m.WorkArea.Top && screenY < m.WorkArea.Bottom);

        if (currentMonitor == null)
        {
            // Point not on any monitor, fall back to primary
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();
            left = primary.WorkArea.Left;
            top = primary.WorkArea.Top;
            right = primary.WorkArea.Right;
            bottom = primary.WorkArea.Bottom;
            return true;
        }

        // Find next monitor (clockwise order based on center positions)
        var currentIndex = monitors.IndexOf(currentMonitor);
        var nextIndex = (currentIndex + 1) % monitors.Count;
        var nextMonitor = monitors[nextIndex];

        left = nextMonitor.WorkArea.Left;
        top = nextMonitor.WorkArea.Top;
        right = nextMonitor.WorkArea.Right;
        bottom = nextMonitor.WorkArea.Bottom;
        return true;
    }

    private class MonitorInfo
    {
        public IntPtr Handle { get; set; }
        public RECT WorkArea { get; set; }
        public RECT MonitorArea { get; set; }
        public bool IsPrimary { get; set; }
    }
}
