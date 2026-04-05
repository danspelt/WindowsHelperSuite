using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsHelperSuite.App;

public partial class ModeMenuWindow : Window
{
    private readonly Action _openFullSettings;
    private readonly Action _openWordsPhrases;
    private int _selectedIndex;
    private readonly List<Border> _rowBorders = [];

    private static readonly string[] RowLabels =
    [
        "1   Words & phrases",
        "2   Settings",
        "3   Cancel",
    ];

    public ModeMenuWindow(Action openFullSettings, Action openWordsPhrases)
    {
        InitializeComponent();
        _openFullSettings = openFullSettings;
        _openWordsPhrases = openWordsPhrases;
        _selectedIndex = 0;
        BuildRows();
        Loaded += (_, _) =>
        {
            UpdateSelectionVisuals();
            Focus();
        };
    }

    private void BuildRows()
    {
        RowsHost.Children.Clear();
        _rowBorders.Clear();

        for (var i = 0; i < RowLabels.Length; i++)
        {
            var idx = i;
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x32)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            border.MouseLeftButtonDown += (_, _) =>
            {
                _selectedIndex = idx;
                UpdateSelectionVisuals();
                ActivateRow(idx);
            };

            border.Child = new TextBlock
            {
                Text = RowLabels[i],
                FontSize = 14,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xF2)),
                TextWrapping = TextWrapping.Wrap,
            };

            RowsHost.Children.Add(border);
            _rowBorders.Add(border);
        }
    }

    private void UpdateSelectionVisuals()
    {
        for (var i = 0; i < _rowBorders.Count; i++)
        {
            var selected = i == _selectedIndex;
            _rowBorders[i].BorderBrush = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x88, 0xFF))
                : System.Windows.Media.Brushes.Transparent;
            _rowBorders[i].BorderThickness = new Thickness(selected ? 2 : 0);
        }
    }

    private void ActivateRow(int index)
    {
        switch (index)
        {
            case 0:
                _openWordsPhrases();
                Close();
                break;
            case 1:
                _openFullSettings();
                Close();
                break;
            case 2:
                Close();
                break;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Up:
                _selectedIndex = (_selectedIndex + RowLabels.Length - 1) % RowLabels.Length;
                UpdateSelectionVisuals();
                e.Handled = true;
                break;
            case Key.Down:
                _selectedIndex = (_selectedIndex + 1) % RowLabels.Length;
                UpdateSelectionVisuals();
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateRow(_selectedIndex);
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                ActivateRow(0);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                ActivateRow(1);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                ActivateRow(2);
                e.Handled = true;
                break;
        }
    }
}
