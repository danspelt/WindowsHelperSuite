using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StillSpace.Counseling;
using StillSpace.Services;
using StillSpace.ViewModels;

namespace StillSpace;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        StillSpaceGlobalHotkey.Attach(this);
        DataContext = _vm = new MainViewModel();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SessionStarted) && _vm.SessionStarted)
                SyncModeCombo();
        };
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.RefreshHeadsetState();
        SyncModeCombo();
    }

    private void SyncModeCombo()
    {
        var want = _vm.SessionMode.ToString();
        foreach (ComboBoxItem item in ModeCombo.Items)
        {
            if (item.Tag is string s && s == want)
            {
                ModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void ModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!Enum.TryParse<CounselingMode>(tag, out var mode)) return;
        _vm.SessionMode = mode;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        e.Handled = true;
        _vm.PushToTalkDown();
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        e.Handled = true;
        _vm.PushToTalkUp();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var clone = _vm.CloneSettingsForEditor();
        var dlg = new SettingsWindow(clone) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ApplySettings(dlg.Result);
    }

    private void OpenCorrection_Click(object sender, RoutedEventArgs e)
    {
        var basis = _vm.CorrectionMistakenBasis;
        if (string.IsNullOrWhiteSpace(basis))
        {
            MessageBox.Show(this, "Nothing to correct yet — type or dictate first.", "Still Space", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new CorrectionWindow(basis) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (string.IsNullOrWhiteSpace(dlg.CorrectedText)) return;
        _vm.SaveCorrection(dlg.MistakenText, dlg.CorrectedText.Trim());
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _vm.Dispose();
}
