using System.IO;
using System.Windows;
using System.Windows.Threading;
using WindowsHelperSuite.App.Services;

namespace WindowsHelperSuite.App;

public partial class App : System.Windows.Application
{
    private ApplicationService? _appService;
    private SingleInstanceService? _singleInstanceService;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsFirstInstance())
        {
            Console.WriteLine("WindowsHelperSuite is already running. Check the system tray for the existing instance.");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            _appService = new ApplicationService();
            _appService.Run();
        }
        catch (Exception ex)
        {
            TryAppendUnhandledLog("Startup", ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appService?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryAppendUnhandledLog("Dispatcher", e.Exception);
    }

    private static void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            TryAppendUnhandledLog("AppDomain", ex);
        }
    }

    private static void TryAppendUnhandledLog(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsHelperSuite",
                "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "unhandled.log");
            File.AppendAllText(
                path,
                $"{DateTime.UtcNow:O} [{source}] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch
        {
            // avoid re-entrancy failures
        }
    }
}

