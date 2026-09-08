using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WinlyStart.Core;

namespace WinlyStart.UI;

/// <summary>
/// Three-month calendar (previous · current · next) with a per-day note. Days that carry a note are
/// tinted amber; the selected day uses the Windows accent. Everything scales with the window (the
/// month grids sit in a Viewbox) and the window size is remembered. Notes import/export as JSON or
/// iCalendar (.ics).
/// </summary>
public partial class CalendarWindow : Window
{
    private readonly CalendarData _data;
    private readonly List<Button> _dayButtons = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private DateTime _month;      // first day of the middle month
    private DateTime _selected;
    private bool _loadingNote;

    private static readonly Brush Amber = Frozen(Color.FromRgb(0xF7, 0xB5, 0x00));
    private static readonly Brush AmberSoft = Frozen(Color.FromRgb(0xFF, 0xE9, 0xB0));
    private static readonly Brush Grey = Frozen(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static Brush Accent => Frozen(Theme.Current.Accent);

    public CalendarWindow()
    {
        InitializeComponent();
        _data = Store.Load("calendar.json", JsonCtx.Default.CalendarData);
        if (_data.Width >= MinWidth && _data.Height >= MinHeight) { Width = _data.Width; Height = _data.Height; }

        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNote(); };

        Render();
        LoadNote();
    }

    // ───────────────────────── month grids ─────────────────────────

    private void Render()
    {
        MonthsHost.Children.Clear();
        _dayButtons.Clear();
        for (int i = -1; i <= 1; i++)
        {
            var panel = BuildMonth(_month.AddMonths(i), isCurrent: i == 0);
            Grid.SetColumn(panel, i + 1);
            MonthsHost.Children.Add(panel);
        }
        HeaderText.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        UpdateCount();
    }

    private Grid BuildMonth(DateTime first, bool isCurrent)
    {
        var fmt = CultureInfo.CurrentCulture.DateTimeFormat;
        var panel = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());

        var title = new TextBlock
        {
            Text = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
            FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = isCurrent ? Accent : Grey, Margin = new Thickness(0, 0, 0, 6),
        };
        panel.Children.Add(title);

        var dow = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        for (int i = 0; i < 7; i++)
        {
            int d = ((int)fmt.FirstDayOfWeek + i) % 7;
            dow.Children.Add(new TextBlock
            {
                Text = fmt.AbbreviatedDayNames[d], FontSize = 11, Foreground = Grey,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        Grid.SetRow(dow, 1);
        panel.Children.Add(dow);

        var days = new UniformGrid { Columns = 7, Rows = 6 };
        int offset = ((int)first.DayOfWeek - (int)fmt.FirstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            var b = new Button
            {
                Style = (Style)FindResource("DayButton"),
                Content = d.Day.ToString(CultureInfo.CurrentCulture),
                Tag = (d, d.Month == first.Month),
            };
            b.Click += Day_Click;
            StyleDay(b);
            _dayButtons.Add(b);
            days.Children.Add(b);
        }
        Grid.SetRow(days, 2);
        panel.Children.Add(days);
        return panel;
    }

    private void StyleDay(Button b)
    {
        var (d, inMonth) = ((DateTime, bool))b.Tag;
        bool hasNote = HasNote(d);
        bool selected = d == _selected;
        bool today = d == DateTime.Today;

        b.Background = selected ? Accent : hasNote ? (inMonth ? Amber : AmberSoft) : Brushes.White;
        b.Foreground = selected ? Brushes.White : inMonth ? Brushes.Black : Grey;
        b.BorderBrush = today ? Accent : selected && hasNote ? Amber : Brushes.Transparent;
        b.FontWeight = hasNote || today || selected ? FontWeights.SemiBold : FontWeights.Normal;
        b.Opacity = inMonth || hasNote || selected ? 1 : 0.55;
        b.ToolTip = hasNote ? _data.Notes[Key(d)] : null;
    }

    private void Restyle()
    {
        foreach (var b in _dayButtons) StyleDay(b);
        UpdateCount();
    }

    private void UpdateCount()
    {
        int n = _data.Notes.Count(kv => !string.IsNullOrWhiteSpace(kv.Value));
        CountText.Text = n == 0 ? string.Empty : n == 1 ? "1 note" : $"{n} notes";
    }

    // ───────────────────────── navigation ─────────────────────────

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (DateTime d, bool) }) return;
        SaveNote();                        // flush the previous day's text first
        _selected = d;
        if (d.Year != _month.Year || d.Month != _month.Month)
        {
            _month = new DateTime(d.Year, d.Month, 1);
            Render();
        }
        else Restyle();
        LoadNote();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Render(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Render(); }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        SaveNote();
        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Render();
        LoadNote();
    }

    // ───────────────────────── notes ─────────────────────────

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private bool HasNote(DateTime d) => _data.Notes.TryGetValue(Key(d), out var t) && !string.IsNullOrWhiteSpace(t);

    private void LoadNote()
    {
        _loadingNote = true;
        NoteBox.Text = _data.Notes.GetValueOrDefault(Key(_selected)) ?? string.Empty;
        NoteTitle.Text = "Note for " + _selected.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
        SavedText.Text = "Saved automatically";
        _loadingNote = false;
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingNote) return;
        SavedText.Text = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNote()
    {
        _saveTimer.Stop();
        string key = Key(_selected), text = NoteBox.Text.Trim();
        bool had = _data.Notes.ContainsKey(key);
        if (text.Length == 0) { if (!had) return; _data.Notes.Remove(key); }
        else
        {
            if (had && _data.Notes[key] == text) return;
            _data.Notes[key] = text;
        }
        Persist();
        Restyle();
        SavedText.Text = "Saved";
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        NoteBox.Text = string.Empty;
        SaveNote();
    }

    private void Persist() => Store.Save("calendar.json", _data, JsonCtx.Default.CalendarData);

    // ───────────────────────── import / export ─────────────────────────

    private const string FileFilter = "Calendar notes (*.json)|*.json|iCalendar (*.ics)|*.ics";

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        SaveNote();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export calendar notes", Filter = FileFilter, DefaultExt = ".json",
            FileName = "WinlyStart-calendar-" + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            bool ics = dlg.FileName.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(dlg.FileName, ics ? ToIcs() : JsonSerializer.Serialize(_data, JsonCtx.Default.CalendarData), new UTF8Encoding(false));   // no BOM: strict .ics readers reject one
            SavedText.Text = $"Exported {_data.Notes.Count} note(s)";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import calendar notes", Filter = FileFilter, CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            int n = dlg.FileName.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)
                ? FromIcs(File.ReadAllText(dlg.FileName))
                : FromJson(File.ReadAllText(dlg.FileName));
            Persist();
            Render();
            LoadNote();
            MessageBox.Show(this, $"Imported {n} note(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private int FromJson(string json)
    {
        var other = JsonSerializer.Deserialize(json, JsonCtx.Default.CalendarData) ?? new CalendarData();
        int n = 0;
        foreach (var (k, v) in other.Notes)
        {
            if (string.IsNullOrWhiteSpace(v) || !DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
            _data.Notes[k] = v.Trim();
            n++;
        }
        return n;
    }

    /// <summary>Every note becomes an all-day VEVENT; the first line is the SUMMARY, the whole note the DESCRIPTION.</summary>
    private string ToIcs()
    {
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Winly Start//Calendar//EN\r\n");
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var (k, v) in _data.Notes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(v) ||
                !DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            string ds = d.ToString("yyyyMMdd", CultureInfo.InvariantCulture), de = d.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string summary = Esc(v.Split('\n')[0].Trim());
            sb.Append("BEGIN:VEVENT\r\n")
              .Append("UID:").Append(ds).Append("@winlystart\r\n")
              .Append("DTSTAMP:").Append(stamp).Append("\r\n")
              .Append("DTSTART;VALUE=DATE:").Append(ds).Append("\r\n")
              .Append("DTEND;VALUE=DATE:").Append(de).Append("\r\n")
              .Append("SUMMARY:").Append(summary).Append("\r\n")
              .Append("DESCRIPTION:").Append(Esc(v)).Append("\r\n")
              .Append("END:VEVENT\r\n");
        }
        return sb.Append("END:VCALENDAR\r\n").ToString();

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
    }

    /// <summary>Reads VEVENTs (all-day or timed); DESCRIPTION wins over SUMMARY. Same-day events are appended.</summary>
    private int FromIcs(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");   // unfold
        int n = 0;
        foreach (var block in text.Split("BEGIN:VEVENT").Skip(1))
        {
            string body = block.Split("END:VEVENT")[0];
            string? start = null, summary = null, desc = null;
            foreach (var raw in body.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
                {
                    var v = line[(line.LastIndexOf(':') + 1)..].Trim();
                    if (v.Length >= 8) start = v[..8];
                }
                else if (line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase)) summary = Unesc(line[8..]);
                else if (line.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase)) desc = Unesc(line[12..]);
            }
            if (start == null || !DateTime.TryParseExact(start, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            string note = (desc ?? summary ?? string.Empty).Trim();
            if (note.Length == 0) continue;
            string key = Key(d);
            _data.Notes[key] = _data.Notes.TryGetValue(key, out var existing) && existing.Length > 0 && existing != note
                ? existing + "\n" + note : note;
            n++;
        }
        return n;

        static string Unesc(string s) => s.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
    }

    // ───────────────────────── window ─────────────────────────

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        _data.Width = Width;
        _data.Height = Height;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveNote();
        // Snapshot the size here too, not only on SizeChanged, so it is remembered even when the
        // window was never resized in this session (or the event was missed).
        if (WindowState == WindowState.Normal && ActualWidth >= MinWidth && ActualHeight >= MinHeight)
        {
            _data.Width = ActualWidth;
            _data.Height = ActualHeight;
        }
        Persist();          // notes (already saved) + remembered size
    }

    // shown with Show(), so IsCancel would not close it — do it explicitly
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
