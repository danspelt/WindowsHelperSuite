using System.Windows;

namespace WindowsHelperSuite.Speech.LiveCaptions;

public partial class LiveCaptionsWindow : Window
{
    private readonly LiveCaptionsViewModel _viewModel;
    private WindowState _preFullscreenState;
    private WindowStyle _preFullscreenStyle;
    private ResizeMode _preFullscreenResizeMode;
    private bool _preFullscreenTopmost;

    public LiveCaptionsWindow(LiveCaptionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Auto-scroll to bottom as new captions arrive.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LiveCaptionsViewModel.DisplayText))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    CaptionScroll?.ScrollToEnd();
                });
            }
        };

        Closed += OnClosed;
    }

    /// <summary>Called by the ViewModel when the user toggles Fullscreen.</summary>
    public void SetFullscreen(bool on)
    {
        if (on)
        {
            _preFullscreenState = WindowState;
            _preFullscreenStyle = WindowStyle;
            _preFullscreenResizeMode = ResizeMode;
            _preFullscreenTopmost = Topmost;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            // Toggle via Normal → Maximized to refresh chromeless bounds correctly.
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            WindowState = WindowState.Maximized;

            // Hide toolbar + status bar for a distraction-free caption wall.
            ToolbarBorder.Visibility = Visibility.Collapsed;
            StatusBarBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = _preFullscreenStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : _preFullscreenStyle;
            ResizeMode = _preFullscreenResizeMode == ResizeMode.NoResize ? ResizeMode.CanResize : _preFullscreenResizeMode;
            WindowState = _preFullscreenState;
            Topmost = _preFullscreenTopmost;

            ToolbarBorder.Visibility = Visibility.Visible;
            StatusBarBorder.Visibility = Visibility.Visible;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}
