using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsHelperSuite.App;

public partial class QuickTextItemEditDialog : Window
{
    public QuickTextItemEditDialog(string title, string hint, string text, string? speakText, bool enabled, bool favorite)
    {
        InitializeComponent();
        Title = title;
        HintText.Text = hint;
        BodyTextBox.Text = text;
        SpeakInput.Text = speakText ?? string.Empty;
        EnabledCheck.IsChecked = enabled;
        FavoriteCheck.IsChecked = favorite;
        SourceInitialized += (_, _) => ApplyNativeDarkFrame();
    }

    private void ApplyNativeDarkFrame()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int one = 1;
        _ = DwmSetWindowAttribute(h, 20, ref one, sizeof(int));
        int border = (0x0F) | (0x0F << 8) | (0x1A << 16);
        _ = DwmSetWindowAttribute(h, 34, ref border, sizeof(int));
        int caption = (0x12) | (0x12 << 8) | (0x1C << 16);
        _ = DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));
        int text = (0xFA) | (0xFA << 8) | (0xFF << 16);
        _ = DwmSetWindowAttribute(h, 36, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public string ResultText => BodyTextBox.Text.Trim();
    public string? ResultSpeakText
    {
        get
        {
            var s = SpeakInput.Text.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }
    }

    public bool ResultEnabled => EnabledCheck.IsChecked == true;
    public bool ResultFavorite => FavoriteCheck.IsChecked == true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Text cannot be empty.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
