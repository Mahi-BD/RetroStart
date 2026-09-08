using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinlyStart.Core;

namespace WinlyStart.UI;

/// <summary>Add, rename, recolour and delete the calendar's note categories.</summary>
public partial class CategoryWindow : Window
{
    /// <summary>One editable row (a working copy — nothing is written until Save).</summary>
    public sealed class Row
    {
        public required string Id { get; init; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#0078D4";
        public Brush Brush => CategoryWindow.BrushOf(Color);
    }

    public sealed class Swatch
    {
        public required string Hex { get; init; }
        public Brush Brush => CategoryWindow.BrushOf(Hex);
        public Thickness Ring { get; set; } = new(0);
    }

    private static readonly string[] Palette =
    {
        "#0078D4", "#8764B8", "#107C10", "#E3008C", "#F7B500", "#D13438",
        "#00B7C3", "#498205", "#C239B3", "#FF8C00", "#8E562E", "#5D5A58",
    };

    private readonly CalendarData _data;
    private readonly ObservableCollection<Row> _rows = new();
    private Row? _current;

    public CategoryWindow(CalendarData data)
    {
        InitializeComponent();
        Dialog.Apply(this);
        _data = data;
        foreach (var c in data.Categories) _rows.Add(new Row { Id = c.Id, Name = c.Name, Color = c.Color });
        List.ItemsSource = _rows;
        if (_rows.Count > 0) List.SelectedIndex = 0;
        RefreshSwatches();
    }

    internal static Brush BrushOf(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch { }
        return Brushes.Gray;
    }

    private void RefreshSwatches()
    {
        Swatches.ItemsSource = Palette
            .Select(h => new Swatch { Hex = h, Ring = new Thickness(string.Equals(h, _current?.Color, StringComparison.OrdinalIgnoreCase) ? 2 : 0) })
            .ToList();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitEditor();
        _current = List.SelectedItem as Row;
        NameBox.Text = _current?.Name ?? string.Empty;
        EditorTitle.Text = _current == null ? "Edit category" : "Editing “" + _current.Name + "”";
        NameBox.IsEnabled = _current != null;
        DeleteButton.IsEnabled = _current != null && _rows.Count > 1;
        RefreshSwatches();
    }

    /// <summary>Copy the editor fields back into the selected row.</summary>
    private void CommitEditor()
    {
        if (_current == null) return;
        string name = NameBox.Text.Trim();
        if (name.Length > 0 && name != _current.Name)
        {
            _current.Name = name;
            Refresh(_current);
        }
    }

    private void Refresh(Row row)
    {
        int i = _rows.IndexOf(row);
        if (i < 0) return;
        _rows[i] = row;               // re-set so the template picks up Name/Brush
        List.SelectedIndex = i;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || (sender as Button)?.Tag is not string hex) return;
        _current.Color = hex;
        Refresh(_current);
        RefreshSwatches();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        CommitEditor();
        var row = new Row
        {
            Id = "cat" + Guid.NewGuid().ToString("N")[..8],
            Name = "New category",
            Color = Palette[_rows.Count % Palette.Length],
        };
        _rows.Add(row);
        List.SelectedItem = row;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || _rows.Count <= 1) return;
        if (MessageBox.Show(this, $"Delete “{_current.Name}”?\n\nDays using it fall back to the first category.",
                "Delete category", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _rows.Remove(_current);
        _current = null;
        List.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitEditor();
        _data.Categories = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new NoteCategory { Id = r.Id, Name = r.Name.Trim(), Color = r.Color })
            .ToList();
        if (_data.Categories.Count == 0) _data.Categories = CalendarData.DefaultCategories();
        DialogResult = true;
    }
}
