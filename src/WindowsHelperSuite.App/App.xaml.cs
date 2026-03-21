using System.Windows;
using WindowsHelperSuite.App.Services;

namespace WindowsHelperSuite.App;

public partial class App : System.Windows.Application
{
    private ApplicationService? _appService;
    private SingleInstanceService? _singleInstanceService;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsFirstInstance())
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _appService = new ApplicationService();
        _appService.Run();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appService?.Dispose();
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}

