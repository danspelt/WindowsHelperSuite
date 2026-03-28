using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.App;

public partial class ModeMenuWindow : Window, IModeMenuKeySink
{
    private readonly IModeManager _modeManager;
    private readonly Action _openSettings;
    private readonly ILoggingService _loggingService;
    private readonly Action<string> _onModeFeedback;
    private bool _focusApplied;

    public ModeMenuWindow(
        IModeManager modeManager,
        Action openSettings,
        ILoggingService loggingService,
        Action<string> onModeFeedback)
    {
        InitializeComponent();
        _modeManager = modeManager;
        _openSettings = openSettings;
        _loggingService = loggingService;
        _onModeFeedback = onModeFeedback;

        var current = ModeDefinition.For(_modeManager.CurrentMode);
        CurrentModeText.Text = $"All features on · saved preference: {current.DisplayName}";

        OptionsList.Items.Add("1  Writer Mode");
        OptionsList.Items.Add("2  Hotkey Mode");
        OptionsList.Items.Add("3  Settings");
        OptionsList.Items.Add("4  Cancel");

        OptionsList.SelectedIndex = _modeManager.CurrentMode == AppMode.Writer ? 0 : 1;

        Loaded += OnLoaded;
        // Backup when the window actually has keyboard focus (no ctrl filter).
        PreviewKeyDown += OnMenuPreviewKeyDown;
    }

    private void OnMenuPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            ActivateSelection();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Called from the low-level keyboard hook when the menu does not have OS focus.
    /// </summary>
    public bool TryConsumeKey(int virtualKey, bool ctrl, bool shift, bool alt)
    {
        if (alt)
        {
            return false;
        }

        // Volume hotkeys — do not steal. (Ctrl may still be down from Ctrl+F3; do not blanket-reject ctrl.)
        if (ctrl && shift && (virtualKey == 0x26 || virtualKey == 0x28))
        {
            return false;
        }

        void Post(Action a) => Dispatcher.BeginInvoke(a, DispatcherPriority.Input);

        switch (virtualKey)
        {
            case 0x1B:
                Post(Close);
                return true;
            case 0x0D:
                Post(ActivateSelection);
                return true;
            case 0x26:
                Post(() => MoveSelection(-1));
                return true;
            case 0x28:
                Post(() => MoveSelection(1));
                return true;
            case 0x31:
                Post(() => ApplyMode(AppMode.Writer));
                return true;
            case 0x32:
                Post(() => ApplyMode(AppMode.Hotkey));
                return true;
            case 0x33:
                Post(OpenSettingsAndClose);
                return true;
            case 0x34:
                Post(Close);
                return true;
            case 0x61:
                Post(() => ApplyMode(AppMode.Writer));
                return true;
            case 0x62:
                Post(() => ApplyMode(AppMode.Hotkey));
                return true;
            case 0x63:
                Post(OpenSettingsAndClose);
                return true;
            case 0x64:
                Post(Close);
                return true;
            default:
                return false;
        }
    }

    private void OpenSettingsAndClose()
    {
        _loggingService.Information("Mode menu: opening Settings");
        Close();
        _openSettings();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Defer until after ShowDialog finishes its layout — avoids re-entrancy crashes during source init.
        Dispatcher.BeginInvoke(ApplyForegroundAndListFocus, DispatcherPriority.Loaded);
    }

    private void ApplyForegroundAndListFocus()
    {
        if (_focusApplied)
        {
            return;
        }

        _focusApplied = true;

        try
        {
            Native.TryBringWindowToForegroundSafe(this);
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Mode menu foreground: {ex.Message}");
        }

        try
        {
            Activate();
            Topmost = true;
            Focus();
            OptionsList.Focus();
            Keyboard.Focus(OptionsList);

            var i = OptionsList.SelectedIndex;
            if (i >= 0 && OptionsList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Mode menu focus: {ex.Message}");
        }
    }

    private void MoveSelection(int delta)
    {
        var i = OptionsList.SelectedIndex;
        if (i < 0)
        {
            i = 0;
        }

        i = Math.Clamp(i + delta, 0, OptionsList.Items.Count - 1);
        OptionsList.SelectedIndex = i;
        OptionsList.SelectedItem = OptionsList.Items[i];
        if (OptionsList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
        {
            item.Focus();
            Keyboard.Focus(item);
        }

        OptionsList.UpdateLayout();
        OptionsList.ScrollIntoView(OptionsList.SelectedItem);
    }

    private void ActivateSelection()
    {
        var i = OptionsList.SelectedIndex;
        if (i < 0 && OptionsList.Items.Count > 0)
        {
            i = 0;
            OptionsList.SelectedIndex = 0;
        }

        switch (i)
        {
            case 0:
                ApplyMode(AppMode.Writer);
                break;
            case 1:
                ApplyMode(AppMode.Hotkey);
                break;
            case 2:
                OpenSettingsAndClose();
                break;
            default:
                _loggingService.Debug("Mode menu closed (Cancel)");
                Close();
                break;
        }
    }

    private void ApplyMode(AppMode mode)
    {
        var result = _modeManager.SwitchMode(mode);
        Close();
        if (!result.Success)
        {
            _loggingService.Warning($"Mode switch failed: {result.ErrorMessage}");
            return;
        }

        _onModeFeedback($"Preference: {ModeDefinition.For(mode).DisplayName}");
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint _);

        [DllImport("user32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        internal static void TryBringWindowToForegroundSafe(Window window)
        {
            var h = new WindowInteropHelper(window).Handle;
            if (h == IntPtr.Zero)
            {
                return;
            }

            var fg = GetForegroundWindow();
            var cur = GetCurrentThreadId();
            var fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0U;

            if (fgThread != 0 && fgThread != cur)
            {
                if (!AttachThreadInput(fgThread, cur, true))
                {
                    SetForegroundWindow(h);
                    return;
                }

                try
                {
                    SetForegroundWindow(h);
                }
                finally
                {
                    AttachThreadInput(fgThread, cur, false);
                }
            }
            else
            {
                SetForegroundWindow(h);
            }
        }
    }
}
