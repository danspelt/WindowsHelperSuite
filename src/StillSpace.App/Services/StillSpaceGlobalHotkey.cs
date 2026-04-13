using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StillSpace.Services;

/// <summary>
/// Registers Ctrl+F4 so Still Space can be raised when running without WindowsHelperSuite
/// (the suite uses a low-level hook and consumes the same chord to focus this process).
/// </summary>
public static class StillSpaceGlobalHotkey
{
    private const int WmHotkey = 0x0312;
    private const int HotKeyId = 0x5354;
    private const uint ModControl = 0x0002;
    private const uint VkF4 = 0x73;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public static void Attach(Window window)
    {
        nint handle = nint.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WmHotkey || wParam.ToInt32() != HotKeyId) return IntPtr.Zero;

            handled = true;
            window.Dispatcher.BeginInvoke(() =>
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Show();
                window.Activate();
            });
            return IntPtr.Zero;
        };

        window.SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero || hook == null) return;
            if (!RegisterHotKey(handle, HotKeyId, ModControl, VkF4))
                return;

            source = HwndSource.FromHwnd(handle);
            source?.AddHook(hook);
        };

        window.Closing += (_, _) =>
        {
            if (handle != nint.Zero)
                UnregisterHotKey(handle, HotKeyId);
            if (source != null && hook != null)
                source.RemoveHook(hook);
        };
    }
}
