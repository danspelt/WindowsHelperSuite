using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.App;

internal static class StillSpaceHotkey
{
    private const string ProcessName = "StillSpace";

    /// <summary>Brings an existing Still Space window forward or starts the app.</summary>
    public static void OpenOrFocus(ILoggingService log)
    {
        try
        {
            if (TryFocusExistingStillSpaceWindow(log))
                return;

            var path = ResolveStillSpaceExecutable();
            if (string.IsNullOrEmpty(path))
            {
                log.Warning(
                    "Still Space hotkey: StillSpace.exe not found next to WindowsHelperSuite or in sibling StillSpace.App build output.");
                return;
            }

            var dir = Path.GetDirectoryName(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir
            });
            log.Information($"Still Space started: {path}");
            QueueFocusAfterStart(log);
        }
        catch (Exception ex)
        {
            log.Warning($"Still Space hotkey failed: {ex.Message}");
        }
    }

    private static string? ResolveStillSpaceExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var sideBySide = Path.Combine(baseDir, $"{ProcessName}.exe");
        if (File.Exists(sideBySide)) return sideBySide;

        try
        {
            // baseDir = ...\Project\bin\Debug\net8.0-windows10.0.19041.0\
            var di = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var tfm = di.Name;
            var configuration = di.Parent?.Name;
            if (string.IsNullOrEmpty(tfm) || string.IsNullOrEmpty(configuration)) return null;

            var src = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var dev = Path.Combine(src, "StillSpace.App", "bin", configuration, tfm, $"{ProcessName}.exe");
            if (File.Exists(dev)) return Path.GetFullPath(dev);
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    /// <summary>Polls until the new main window can receive foreground (hook thread cannot always focus immediately).</summary>
    private static void QueueFocusAfterStart(ILoggingService log)
    {
        _ = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    if (TryFocusExistingStillSpaceWindow(null))
                    {
                        log.Information("Still Space focused after start.");
                        return;
                    }

                    Thread.Sleep(50);
                }

                log.Debug("Still Space started; focus was not applied within the retry window.");
            }
            catch (Exception ex)
            {
                log.Debug($"Still Space post-start focus: {ex.Message}");
            }
        });
    }

    /// <summary>Focus apphost StillSpace.exe or any process whose main window title is Still Space (e.g. dotnet run).</summary>
    private static bool TryFocusExistingStillSpaceWindow(ILoggingService? log)
    {
        var byName = Process.GetProcessesByName(ProcessName);
        try
        {
            foreach (var p in byName)
            {
                if (TryFocusProcessMainWindow(p, log, "Still Space (apphost)"))
                    return true;
            }
        }
        finally
        {
            foreach (var p in byName)
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        const string titleNeedle = "Still Space";
        Process[]? all = null;
        try
        {
            all = Process.GetProcesses();
            foreach (var p in all)
            {
                try
                {
                    if (p.MainWindowHandle == nint.Zero || string.IsNullOrEmpty(p.MainWindowTitle))
                        continue;
                    if (!p.MainWindowTitle.Contains(titleNeedle, StringComparison.Ordinal))
                        continue;
                    if (TryFocusProcessMainWindow(p, log, $"title match: {p.ProcessName}"))
                        return true;
                }
                catch
                {
                    /* ignore */
                }
            }
        }
        finally
        {
            if (all != null)
            {
                foreach (var p in all)
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }
        }

        return false;
    }

    private static bool TryFocusProcessMainWindow(Process p, ILoggingService? log, string reason)
    {
        try
        {
            if (p.MainWindowHandle == nint.Zero)
                return false;
            var h = p.MainWindowHandle;
            if (!Win32WindowActivation.TryForceForegroundWindow(h))
                return false;
            log?.Information($"Still Space brought to foreground ({reason}).");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
