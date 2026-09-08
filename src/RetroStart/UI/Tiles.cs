using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using RetroStart.Core;

namespace RetroStart.UI;

/// <summary>One tile on the board. Geometry is in 48 px units with 4 px gutters, like Windows 10.</summary>
public sealed class TileVm : INotifyPropertyChanged
{
    public const int Unit = 48, Gap = 4, Pitch = Unit + Gap;

    public AppEntry App { get; }
    public TileGroupVm Group { get; set; } = null!;

    private TileSize _size;
    private int _col, _row;
    private bool _dragging;

    public TileVm(AppEntry app, TileSize size, int col, int row)
    {
        App = app; _size = size; _col = col; _row = row;
        app.PropertyChanged += (_, e) => { if (e.PropertyName == "Item[]") Raise(nameof(Image)); };
    }

    public TileSize Size
    {
        get => _size;
        set { _size = value; Raise(nameof(Size)); Raise(nameof(Width)); Raise(nameof(Height)); Raise(nameof(ShowLabel)); Raise(nameof(ImageSize)); Raise(nameof(ImageWidth)); Raise(nameof(ImageHeight)); Raise(nameof(Image)); }
    }
    public int Col { get => _col; set { _col = value; Raise(nameof(X)); } }
    public int Row { get => _row; set { _row = value; Raise(nameof(Y)); } }
    public bool IsDragging { get => _dragging; set { _dragging = value; Raise(nameof(IsDragging)); } }

    public int Cols => _size switch { TileSize.Small => 1, TileSize.Medium => 2, _ => 4 };
    public int Rows => _size switch { TileSize.Small => 1, TileSize.Medium or TileSize.Wide => 2, _ => 4 };
    public double Width => Cols * Pitch - Gap;
    public double Height => Rows * Pitch - Gap;
    public double X => _col * Pitch;
    public double Y => _row * Pitch;
    public bool ShowLabel => _size != TileSize.Small;
    public string Name => App.Name;
    public bool CanRunAsAdmin => !App.IsPackaged;

    /// <summary>Packaged apps ship a square logo on a coloured plate → fill the tile like Windows 10 did.
    /// Desktop apps get their icon centred.</summary>
    public int ImageSize => App.IsPackaged
        ? _size switch { TileSize.Small => 48, TileSize.Large => 204, _ => 100 }
        : _size switch { TileSize.Small => 24, TileSize.Large => 96, _ => 48 };
    public double ImageWidth => App.IsPackaged && _size != TileSize.Wide ? Width : ImageSize;
    public double ImageHeight => App.IsPackaged && _size != TileSize.Wide ? Height : ImageSize;
    public BitmapSource? Image => App[ImageSize];

    public Tile ToModel() => new() { AppId = App.Id, Size = _size, Col = _col, Row = _row };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>A named group of tiles; packs its tiles first-fit into a grid <see cref="Columns"/> units wide.</summary>
public sealed class TileGroupVm : INotifyPropertyChanged
{
    private string _name;
    private int _rows;

    public TileGroupVm(string name, int columns) { _name = name; _columns = columns; }

    public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
    private int _columns;
    public int Columns { get => _columns; set { _columns = value; Raise(nameof(PixelWidth)); } }
    public ObservableCollection<TileVm> Tiles { get; } = new();
    public double PixelWidth => Columns * TileVm.Pitch - TileVm.Gap;
    public double PixelHeight => Math.Max(_rows, 2) * TileVm.Pitch - TileVm.Gap;

    public void Add(TileVm t) { t.Group = this; Tiles.Add(t); Pack(); }
    public void Remove(TileVm t) { Tiles.Remove(t); Pack(); }

    /// <summary>
    /// Lays the tiles out. <paramref name="priority"/> (a dropped tile) is placed first at its requested
    /// cell; every other tile keeps its cell when still free, otherwise slides to the first free slot.
    /// </summary>
    public void Pack(TileVm? priority = null)
    {
        var grid = new List<bool[]>();
        bool Free(int c, int r, int w, int h)
        {
            if (c < 0 || c + w > Columns || r < 0) return false;
            for (int y = r; y < r + h; y++)
            {
                if (y >= grid.Count) return true;
                for (int x = c; x < c + w; x++) if (grid[y][x]) return false;
            }
            return true;
        }
        void Occupy(int c, int r, int w, int h)
        {
            while (grid.Count < r + h) grid.Add(new bool[Columns]);
            for (int y = r; y < r + h; y++) for (int x = c; x < c + w; x++) grid[y][x] = true;
        }
        void Place(TileVm t, bool keep)
        {
            if (keep && t.Row <= 64 && Free(t.Col, t.Row, t.Cols, t.Rows)) { Occupy(t.Col, t.Row, t.Cols, t.Rows); return; }
            for (int r = 0; ; r++)
                for (int c = 0; c + t.Cols <= Columns; c++)
                    if (Free(c, r, t.Cols, t.Rows)) { t.Col = c; t.Row = r; Occupy(c, r, t.Cols, t.Rows); return; }
        }

        var order = Tiles.OrderBy(t => t.Row).ThenBy(t => t.Col).ToList();
        if (priority != null)
        {
            order.Remove(priority);
            priority.Col = Math.Clamp(priority.Col, 0, Math.Max(0, Columns - priority.Cols));
            priority.Row = Math.Max(0, priority.Row);
            Place(priority, keep: true);
        }
        foreach (var t in order) Place(t, keep: true);

        _rows = grid.Count;
        Raise(nameof(PixelHeight));
    }

    public TileGroup ToModel() => new() { Name = _name, Tiles = Tiles.Select(t => t.ToModel()).ToList() };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public static class TileBoard
{
    public static ObservableCollection<TileGroupVm> Load(TileLayout layout, AppCatalog catalog, int columns)
    {
        var groups = new ObservableCollection<TileGroupVm>();
        foreach (var g in layout.Groups)
        {
            var vm = new TileGroupVm(g.Name, columns);
            foreach (var t in g.Tiles)
            {
                var app = catalog.Find(t.AppId);
                if (app == null) continue;                       // uninstalled since last run
                vm.Tiles.Add(new TileVm(app, t.Size, t.Col, t.Row) { Group = vm });
            }
            vm.Pack();
            groups.Add(vm);
        }
        return groups;
    }

    public static TileLayout Save(IEnumerable<TileGroupVm> groups) => new() { Groups = groups.Select(g => g.ToModel()).ToList() };

    /// <summary>First-run board, mirroring the spirit of the Windows 10 default groups with whatever is installed.</summary>
    public static TileLayout Default(AppCatalog catalog)
    {
        TileGroup Group(string name, params (string app, TileSize size)[] wants)
        {
            var g = new TileGroup { Name = name };
            foreach (var (want, size) in wants)
            {
                var app = catalog.Apps.FirstOrDefault(a => a.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                       ?? catalog.Apps.FirstOrDefault(a => a.Name.StartsWith(want, StringComparison.OrdinalIgnoreCase));
                if (app != null && g.Tiles.All(t => t.AppId != app.Id)) g.Tiles.Add(new Tile { AppId = app.Id, Size = size });
            }
            return g;
        }
        var layout = new TileLayout();
        layout.Groups.Add(Group("Productivity",
            ("Microsoft Edge", TileSize.Wide), ("Mail", TileSize.Medium), ("Calendar", TileSize.Medium),
            ("Calculator", TileSize.Medium), ("Microsoft Store", TileSize.Medium), ("Photos", TileSize.Medium),
            ("Settings", TileSize.Medium), ("Notepad", TileSize.Medium), ("Paint", TileSize.Medium)));
        layout.Groups.Add(Group("Explore",
            ("Weather", TileSize.Wide), ("News", TileSize.Medium), ("Clock", TileSize.Medium),
            ("Camera", TileSize.Medium), ("Snipping Tool", TileSize.Medium), ("Microsoft To Do", TileSize.Medium)));
        layout.Groups.RemoveAll(g => g.Tiles.Count == 0);
        return layout;
    }
}
