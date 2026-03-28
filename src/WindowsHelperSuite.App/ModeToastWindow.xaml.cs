using System.Windows;
using System.Windows.Threading;

namespace WindowsHelperSuite.App;

public partial class ModeToastWindow : Window
{
    private ModeToastWindow(string message, TimeSpan duration)
    {
        InitializeComponent();
        MessageText.Text = message;
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Top + (wa.Height - ActualHeight) / 2;
        };

        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        Loaded += (_, _) => timer.Start();
    }

    public static void ShowBrief(string message, double seconds = 2.2)
    {
        var w = new ModeToastWindow(message, TimeSpan.FromSeconds(seconds));
        w.Show();
    }
}
