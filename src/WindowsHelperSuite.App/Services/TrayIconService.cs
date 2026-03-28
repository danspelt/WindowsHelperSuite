using System.Windows;
using System.Windows.Forms;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.App.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ILoggingService _loggingService;
    private Window? _settingsWindow;

    public TrayIconService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Windows Helper Suite",
            Visible = true
        };

        CreateContextMenu();
    }

    private void CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings", null, OnSettingsClick);
        var exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += OnSettingsClick;
    }

    /// <summary>Tray text is limited to 63 characters on Windows.</summary>
    public void ApplyModeIndicator(AppMode _)
    {
        const string text = "Windows Helper Suite — Writer + hotkeys";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowSettings()
    {
        OnSettingsClick(this, EventArgs.Empty);
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        _loggingService.Information("Opening settings window");

        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new MainWindow();
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        _loggingService.Information("Exiting application via tray menu");
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
