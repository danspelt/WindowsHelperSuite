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
    private ToolStripMenuItem? _reEnableWriterItem;

    /// <summary>Overlay service reference used to offer "Re-enable Writer" when suppressed.</summary>
    public IOverlayService? OverlayService { get; set; }

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
        _reEnableWriterItem = new ToolStripMenuItem("Re-enable Writer", null, OnReEnableWriterClick)
        {
            Visible = false
        };
        var exitItem = new ToolStripMenuItem("Exit", null, OnExitClick);

        contextMenu.Items.Add(chatItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_reEnableWriterItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(wordsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        contextMenu.Opening += (_, _) => RefreshSuppressionMenuItem();

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += OnSettingsClick;
    }

    private void RefreshSuppressionMenuItem()
    {
        if (_reEnableWriterItem == null) return;
        var ov = OverlayService;
        if (ov?.IsSuppressed == true && ov.SuppressedUntilUtc is DateTime until)
        {
            var remaining = until - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            var mins = (int)Math.Ceiling(remaining.TotalMinutes);
            _reEnableWriterItem.Text = $"Re-enable Writer ({mins} min left)";
            _reEnableWriterItem.Visible = true;
        }
        else
        {
            _reEnableWriterItem.Visible = false;
        }
    }

    private void OnReEnableWriterClick(object? sender, EventArgs e)
    {
        OverlayService?.ClearSuppression();
        _loggingService.Information("Writer re-enabled via tray menu");
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
