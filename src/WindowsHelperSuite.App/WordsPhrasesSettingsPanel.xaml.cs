using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Infrastructure.Services;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace WindowsHelperSuite.App;

public partial class WordsPhrasesSettingsPanel : System.Windows.Controls.UserControl
{
    private QuickTextSettings _model = null!;
    private string _wordFilter = "";
    private string _phraseFilter = "";

    public WordsPhrasesSettingsPanel()
    {
        InitializeComponent();
    }

    public void Attach(QuickTextSettings model)
    {
        _model = model;
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshWords();
        RefreshPhrases();
    }

    private void WordSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _wordFilter = WordSearchBox.Text.Trim();
        RefreshWords();
    }

    private void PhraseSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _phraseFilter = PhraseSearchBox.Text.Trim();
        RefreshPhrases();
    }

    private IEnumerable<QuickWordItem> WordsDisplayOrder() =>
        _model.Words
            .Where(w => string.IsNullOrEmpty(_wordFilter) ||
                        w.Text.Contains(_wordFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.IsFavorite)
            .ThenBy(w => w.SortOrder)
            .ThenBy(w => w.Text, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<QuickPhraseItem> PhrasesDisplayOrder() =>
        _model.Phrases
            .Where(p => string.IsNullOrEmpty(_phraseFilter) ||
                        p.Text.Contains(_phraseFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.IsFavorite)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Text, StringComparer.OrdinalIgnoreCase);

    private void RefreshWords()
    {
        WordsHost.Children.Clear();
        var n = 1;
        foreach (var w in WordsDisplayOrder())
        {
            WordsHost.Children.Add(BuildWordRow(n++, w));
        }
    }

    private void RefreshPhrases()
    {
        PhrasesHost.Children.Clear();
        var n = 1;
        foreach (var p in PhrasesDisplayOrder())
        {
            PhrasesHost.Children.Add(BuildPhraseRow(n++, p));
        }
    }

    private UIElement BuildWordRow(int index, QuickWordItem w)
    {
        return BuildItemRow(
            index, w.Text, w.IsFavorite, w.IsEnabled,
            isEnabled => w.IsEnabled = isEnabled,
            () => EditWord(w),
            () => MoveWord(w, -1),
            () => MoveWord(w, 1),
            () => ToggleFavoriteWord(w),
            () => DeleteWord(w),
            TextTrimming.CharacterEllipsis,
            TextWrapping.NoWrap);
    }

    private UIElement BuildPhraseRow(int index, QuickPhraseItem p)
    {
        return BuildItemRow(
            index, p.Text, p.IsFavorite, p.IsEnabled,
            isEnabled => p.IsEnabled = isEnabled,
            () => EditPhrase(p),
            () => MovePhrase(p, -1),
            () => MovePhrase(p, 1),
            () => ToggleFavoritePhrase(p),
            () => DeletePhrase(p),
            TextTrimming.None,
            TextWrapping.Wrap);
    }

    private static UIElement BuildItemRow(
        int index, string itemText, bool isFavorite, bool isEnabled,
        Action<bool> setEnabled,
        Action onEdit, Action onMoveUp, Action onMoveDown,
        Action onToggleFav, Action onDelete,
        TextTrimming trimming, TextWrapping wrapping)
    {
        var normalBg = Color.FromRgb(0x1C, 0x1C, 0x30);
        var hoverBg = Color.FromRgb(0x22, 0x22, 0x3C);

        var border = new Border
        {
            Background = new SolidColorBrush(normalBg),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x48)),
        };

        border.MouseEnter += (_, _) => border.Background = new SolidColorBrush(hoverBg);
        border.MouseLeave += (_, _) => border.Background = new SolidColorBrush(normalBg);

        var outerGrid = new Grid();
        if (isFavorite)
        {
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var accentStripe = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0xE0)),
                CornerRadius = new CornerRadius(10, 0, 0, 10),
                Width = 4,
            };
            Grid.SetColumn(accentStripe, 0);
            outerGrid.Children.Add(accentStripe);
        }
        else
        {
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var innerGrid = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var idxBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x28)),
            CornerRadius = new CornerRadius(5),
            Width = 24, Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var idx = new TextBlock
        {
            Text = index.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x8A)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = System.Windows.FontWeights.SemiBold,
        };
        idxBorder.Child = idx;
        Grid.SetColumn(idxBorder, 0);

        var textPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 12, 0),
        };
        var starBlock = new TextBlock
        {
            Text = isFavorite ? "★" : "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0xE0)),
            FontSize = 13,
            Margin = new Thickness(0, 0, isFavorite ? 6 : 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var textBlock = new TextBlock
        {
            Text = itemText,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = trimming,
            TextWrapping = wrapping,
            FontSize = 13,
        };
        if (isFavorite) textPanel.Children.Add(starBlock);
        textPanel.Children.Add(textBlock);
        Grid.SetColumn(textPanel, 1);

        var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var enabled = new System.Windows.Controls.CheckBox
        {
            IsChecked = isEnabled,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xC8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        enabled.Checked += (_, _) => setEnabled(true);
        enabled.Unchecked += (_, _) => setEnabled(false);
        right.Children.Add(enabled);
        right.Children.Add(NewSmallBtn("Edit", onEdit));
        right.Children.Add(NewSmallBtn("▲", onMoveUp, isCompact: true));
        right.Children.Add(NewSmallBtn("▼", onMoveDown, isCompact: true));
        right.Children.Add(NewSmallBtn(isFavorite ? "★" : "☆", onToggleFav, isCompact: true,
            accentFg: isFavorite ? Color.FromRgb(0x6B, 0x6B, 0xE0) : (Color?)null));
        right.Children.Add(NewSmallBtn("Delete", onDelete, danger: true));
        Grid.SetColumn(right, 2);

        innerGrid.Children.Add(idxBorder);
        innerGrid.Children.Add(textPanel);
        innerGrid.Children.Add(right);

        Grid.SetColumn(innerGrid, isFavorite ? 1 : 0);
        outerGrid.Children.Add(innerGrid);
        border.Child = outerGrid;
        return border;
    }

    private static System.Windows.Controls.Button NewSmallBtn(
        string content, Action onClick,
        bool danger = false, bool isCompact = false, System.Windows.Media.Color? accentFg = null)
    {
        var normalBg = danger
            ? Color.FromRgb(0x3A, 0x1E, 0x2A)
            : Color.FromRgb(0x20, 0x20, 0x3C);
        var hoverBgColor = danger
            ? Color.FromRgb(0x50, 0x2A, 0x3A)
            : Color.FromRgb(0x30, 0x30, 0x58);
        var pressBgColor = danger
            ? Color.FromRgb(0x2A, 0x14, 0x1E)
            : Color.FromRgb(0x18, 0x18, 0x30);
        var fgColor = accentFg ?? (danger
            ? Color.FromRgb(0xF0, 0xA0, 0xB0)
            : Color.FromRgb(0xA0, 0xA0, 0xC8));

        var bd = new Border
        {
            Background = new SolidColorBrush(normalBg),
            CornerRadius = new CornerRadius(6),
            Padding = isCompact ? new Thickness(6, 4, 6, 4) : new Thickness(10, 5, 10, 5),
        };
        var label = new TextBlock
        {
            Text = content,
            Foreground = new SolidColorBrush(fgColor),
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bd.Child = label;

        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
        var bdFactory = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree = bdFactory;

        var b = new System.Windows.Controls.Button
        {
            Content = bd,
            Margin = new Thickness(3, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Template = template,
        };

        b.MouseEnter += (_, _) => bd.Background = new SolidColorBrush(hoverBgColor);
        b.MouseLeave += (_, _) => bd.Background = new SolidColorBrush(normalBg);
        b.PreviewMouseLeftButtonDown += (_, _) => bd.Background = new SolidColorBrush(pressBgColor);
        b.PreviewMouseLeftButtonUp += (_, _) => bd.Background = new SolidColorBrush(hoverBgColor);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void EditWord(QuickWordItem w)
    {
        var dlg = new QuickTextItemEditDialog(
            "Edit word",
            "Single word or short token inserted into the focused field.",
            w.Text,
            w.SpeakText,
            w.IsEnabled,
            w.IsFavorite)
        {
            Owner = Window.GetWindow(this),
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        w.Text = dlg.ResultText;
        w.SpeakText = dlg.ResultSpeakText;
        w.IsEnabled = dlg.ResultEnabled;
        w.IsFavorite = dlg.ResultFavorite;
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshWords();
    }

    private void EditPhrase(QuickPhraseItem p)
    {
        var dlg = new QuickTextItemEditDialog(
            "Edit phrase",
            "Full phrase inserted when selected.",
            p.Text,
            p.SpeakText,
            p.IsEnabled,
            p.IsFavorite)
        {
            Owner = Window.GetWindow(this),
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        p.Text = dlg.ResultText;
        p.SpeakText = dlg.ResultSpeakText;
        p.IsEnabled = dlg.ResultEnabled;
        p.IsFavorite = dlg.ResultFavorite;
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshPhrases();
    }

    private void MoveWord(QuickWordItem item, int dir)
    {
        var ordered = _model.Words.OrderBy(w => w.SortOrder).ToList();
        var i = ordered.IndexOf(item);
        if (i < 0)
        {
            return;
        }

        var j = i + dir;
        if (j < 0 || j >= ordered.Count)
        {
            return;
        }

        var a = ordered[i];
        var b = ordered[j];
        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshWords();
    }

    private void MovePhrase(QuickPhraseItem item, int dir)
    {
        var ordered = _model.Phrases.OrderBy(p => p.SortOrder).ToList();
        var i = ordered.IndexOf(item);
        if (i < 0)
        {
            return;
        }

        var j = i + dir;
        if (j < 0 || j >= ordered.Count)
        {
            return;
        }

        var a = ordered[i];
        var b = ordered[j];
        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshPhrases();
    }

    private void ToggleFavoriteWord(QuickWordItem w)
    {
        w.IsFavorite = !w.IsFavorite;
        RefreshWords();
    }

    private void ToggleFavoritePhrase(QuickPhraseItem p)
    {
        p.IsFavorite = !p.IsFavorite;
        RefreshPhrases();
    }

    private void DeleteWord(QuickWordItem w)
    {
        if (System.Windows.MessageBox.Show(
                Window.GetWindow(this)!,
                $"Delete word \"{w.Text}\"?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _model.Words.Remove(w);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshWords();
    }

    private void DeletePhrase(QuickPhraseItem p)
    {
        if (System.Windows.MessageBox.Show(
                Window.GetWindow(this)!,
                "Delete this phrase?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _model.Phrases.Remove(p);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshPhrases();
    }

    private int NextWordSort() =>
        _model.Words.Count == 0 ? 0 : _model.Words.Max(w => w.SortOrder) + 1;

    private int NextPhraseSort() =>
        _model.Phrases.Count == 0 ? 0 : _model.Phrases.Max(p => p.SortOrder) + 1;

    private void AddWordBtn_Click(object sender, RoutedEventArgs e)
    {
        var w = new QuickWordItem
        {
            Id = Guid.NewGuid(),
            Text = "",
            SpeakText = null,
            IsEnabled = true,
            IsFavorite = false,
            SortOrder = NextWordSort(),
        };
        var dlg = new QuickTextItemEditDialog(
            "Add word",
            "Single word or short token.",
            "New word",
            null,
            true,
            false)
        {
            Owner = Window.GetWindow(this),
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        w.Text = dlg.ResultText;
        w.SpeakText = dlg.ResultSpeakText;
        w.IsEnabled = dlg.ResultEnabled;
        w.IsFavorite = dlg.ResultFavorite;
        _model.Words.Add(w);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshWords();
    }

    private void AddPhraseBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = new QuickPhraseItem
        {
            Id = Guid.NewGuid(),
            Text = "",
            SpeakText = null,
            IsEnabled = true,
            IsFavorite = false,
            SortOrder = NextPhraseSort(),
        };
        var dlg = new QuickTextItemEditDialog(
            "Add phrase",
            "Full sentence or phrase.",
            "New phrase",
            null,
            true,
            false)
        {
            Owner = Window.GetWindow(this),
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        p.Text = dlg.ResultText;
        p.SpeakText = dlg.ResultSpeakText;
        p.IsEnabled = dlg.ResultEnabled;
        p.IsFavorite = dlg.ResultFavorite;
        _model.Phrases.Add(p);
        QuickTextSettingsService.RepairAfterImport(_model);
        RefreshPhrases();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = "quick-text-export.json",
        };

        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var payload = QuickTextSettingsService.Clone(_model);
            File.WriteAllText(dlg.FileName, QuickTextSettingsService.Serialize(payload));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json|All files|*.*" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var imported = QuickTextSettingsService.Deserialize(json);
            QuickTextSettingsService.RepairAfterImport(imported);
            _model.Words.Clear();
            _model.Phrases.Clear();
            foreach (var w in imported.Words)
            {
                _model.Words.Add(w);
            }

            foreach (var p in imported.Phrases)
            {
                _model.Phrases.Add(p);
            }

            QuickTextSettingsService.RepairAfterImport(_model);
            RefreshAll();
            System.Windows.MessageBox.Show(Window.GetWindow(this)!, "Import completed.", "Words & phrases", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Window.GetWindow(this)!, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetFactory_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                Window.GetWindow(this)!,
                "Replace all words and phrases with the app default starter lists?",
                "Reset to defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        QuickTextSettingsService.ResetToFactoryDefaults(_model);
        RefreshAll();
    }

}
