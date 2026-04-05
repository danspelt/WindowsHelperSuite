using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using WindowsHelperSuite.App.ViewModels;

namespace WindowsHelperSuite.App.Views;

public partial class ChatSettingsWindow : Window
{
    private readonly ChatSettingsViewModel _vm;

    public ChatSettingsWindow(ChatSettingsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyNativeDarkFrame();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // PasswordBox doesn't support binding — sync manually
        ApiKeyBox.Password = _vm.ApiKey;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ApiKey = ApiKeyBox.Password;
    }

    private void SaveAndClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelAndClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
}

public class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
