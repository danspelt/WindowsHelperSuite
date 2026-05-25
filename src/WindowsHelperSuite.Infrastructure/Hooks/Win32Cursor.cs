using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Hooks;

public static class Win32Cursor
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static bool TryGetPosition(out int x, out int y)
    {
        if (!GetCursorPos(out var pt))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = pt.X;
        y = pt.Y;
        return true;
    }
}
