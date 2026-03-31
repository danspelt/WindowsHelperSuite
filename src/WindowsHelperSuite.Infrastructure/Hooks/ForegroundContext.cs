using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Infrastructure.Hooks;

/// <summary>Foreground window process + title for Writer context (lightweight; no UI Automation).</summary>
public static class ForegroundContext
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    public static WriterContextSnapshot GetWriterSnapshot()
    {
        string? processName = null;
        string? title = null;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != nint.Zero)
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0)
                {
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        processName = p.ProcessName;
                    }
                    catch
                    {
                        // Process may have exited
                    }
                }

                var sb = new StringBuilder(512);
                if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
                {
                    title = sb.ToString();
                }
            }
        }
        catch
        {
            // Never throw — overlay must stay responsive
        }

        var mode = MapProcessToTypingMode(processName);
        return new WriterContextSnapshot(mode, processName, title);
    }

    private static WriterTypingMode MapProcessToTypingMode(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return WriterTypingMode.Neutral;
        }

        var p = processName.ToLowerInvariant();

        if (p is "devenv" or "code" or "cursor" or "rider64" or "notepad++" or "windowsterminal" or "wt"
            or "dotnet" or "powershell" or "pwsh" or "vscode")
        {
            return WriterTypingMode.Development;
        }

        if (p is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" or "slack" or "discord" or "teams" or "ms-teams")
        {
            return WriterTypingMode.Chat;
        }

        if (p is "outlook" or "olk" or "thunderbird")
        {
            return WriterTypingMode.Email;
        }

        return WriterTypingMode.Neutral;
    }
}
