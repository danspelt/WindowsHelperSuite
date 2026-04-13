using System.IO;
using System.Windows;
using StillSpace.Services;

namespace StillSpace;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var baseDir = AppContext.BaseDirectory;
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillSpace");
        Directory.CreateDirectory(appDataDir);
        DotEnvLoader.TryLoad(
            Path.Combine(baseDir, ".env"),
            Path.Combine(Environment.CurrentDirectory, ".env"),
            Path.Combine(appDataDir, ".env"));
        base.OnStartup(e);
    }
}
