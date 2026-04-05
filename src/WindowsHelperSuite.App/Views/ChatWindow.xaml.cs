using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsHelperSuite.App.ViewModels;

namespace WindowsHelperSuite.App.Views;

public partial class ChatWindow : Window
{
    private readonly ChatViewModel _vm;

    public ChatWindow(ChatViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        _vm.PropertyChanged += Vm_PropertyChanged;
        _vm.Messages.CollectionChanged += Messages_CollectionChanged;
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyNativeDarkFrame();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeAsync();
        InputBox.Focus();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.IsStreaming):
                Dispatcher.Invoke(() =>
                {
                    CancelBtn.Visibility = _vm.IsStreaming ? Visibility.Visible : Visibility.Collapsed;
                });
                break;
            case nameof(ChatViewModel.ErrorMessage):
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(_vm.ErrorMessage))
                    {
                        ErrorText.Text = _vm.ErrorMessage;
                        ErrorBar.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ErrorBar.Visibility = Visibility.Collapsed;
                    }
                });
                break;
        }

        // Auto-scroll on streaming content updates
        if (e.PropertyName == nameof(ChatViewModel.IsBusy))
        {
            Dispatcher.InvokeAsync(ScrollToBottom,
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(ScrollToBottom,
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ScrollToBottom()
    {
        if (MessageList.Items.Count > 0)
        {
            MessageList.ScrollIntoView(MessageList.Items[^1]);
        }
    }

    private void InputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (_vm.SendCommand.CanExecute(null))
            {
                _vm.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_vm.CancelGenerationCommand.CanExecute(null))
            {
                _vm.CancelGenerationCommand.Execute(null);
            }
        }
        // Shift+Enter = allow default newline (AcceptsReturn is True)
    }

    private void ApplyNativeDarkFrame()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int one = 1;
        _ = DwmSetWindowAttribute(h, 20, ref one, sizeof(int));
        int border = (0x0F) | (0x0F << 8) | (0x1A << 16);
        _ = DwmSetWindowAttribute(h, 34, ref border, sizeof(int));
        int caption = (0x0C) | (0x0C << 8) | (0x18 << 16);
        _ = DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));
        int text = (0xFA) | (0xFA << 8) | (0xFF << 16);
        _ = DwmSetWindowAttribute(h, 36, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm.Messages.CollectionChanged -= Messages_CollectionChanged;
        base.OnClosed(e);
    }
}
