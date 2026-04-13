using System.Windows;

namespace StillSpace;

public partial class CorrectionWindow : Window
{
    public string MistakenText { get; }
    public string CorrectedText => CorrectedBox.Text;

    public CorrectionWindow(string mistaken)
    {
        InitializeComponent();
        MistakenText = mistaken;
        MistakenBox.Text = mistaken;
        CorrectedBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CorrectedBox.Text))
        {
            MessageBox.Show(this, "Enter the corrected phrase.", "Still Space", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
