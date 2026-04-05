using System.Windows;
using System.Windows.Forms;
using WindowsHelperSuite.App;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.App.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private readonly Action _reloadHotkeys;
    private readonly Action? _openChat;
    private Window? _settingsWindow;

    public TrayIconService(ILoggingService loggingService, ISettingsService settingsService, Action reloadHotkeys, Action? openChat = null)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;
        _reloadHotkeys = reloadHotkeys;
        _openChat = openChat;
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

        var chatItem = new ToolStripMenuItem("AI Chat", null, (_, _) => _openChat?.Invoke());
        var settingsItem = new ToolStripMenuItem("Settings", null, OnSettingsClick);
        var wordsItem = new ToolStripMenuItem("Words & phrases…", null, (_, _) => ShowSettings(HotkeySettingsWindow.TabWordsPhrases));
        var exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

        contextMenu.Items.Add(chatItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(wordsItem);
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

    public void ShowSettings() => ShowSettings(null);

    public void ShowSettings(int? initialTabIndex)
    {
        _loggingService.Information(
            initialTabIndex is int t
                ? $"Opening settings (tab index {t})"
                : "Opening hotkey settings window");

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null && _settingsWindow.IsVisible)
            {
                if (initialTabIndex is int idx && _settingsWindow is HotkeySettingsWindow existing)
                {
                    existing.NavigateToTab(idx);
                }

                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new HotkeySettingsWindow(_settingsService, _reloadHotkeys, initialTabIndex);
            _settingsWindow.Closed += (s, _) => _settingsWindow = null;
            _settingsWindow.Show();
        });
    }

    private void OnSettingsClick(object? sender, EventArgs e) => ShowSettings(null);

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
