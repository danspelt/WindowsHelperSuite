using System.Runtime.InteropServices;

namespace WindowsHelperSuite.Infrastructure.Audio;

public static class Win32Audio
{
    public const uint VK_VOLUME_UP = 0xAF;
    public const uint VK_VOLUME_DOWN = 0xAE;
    public const uint VK_VOLUME_MUTE = 0xAD;

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void VolumeUp()
    {
        keybd_event((byte)VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VK_VOLUME_UP, 0, 2, UIntPtr.Zero);
    }

    public static void VolumeDown()
    {
        keybd_event((byte)VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VK_VOLUME_DOWN, 0, 2, UIntPtr.Zero);
    }

    public static void VolumeMute()
    {
        keybd_event((byte)VK_VOLUME_MUTE, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VK_VOLUME_MUTE, 0, 2, UIntPtr.Zero);
    }
}
