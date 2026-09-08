using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinlyStart.Core;

namespace WinlyStart.UI;

public partial class StartMenuWindow : Window
{
    private const double RowHeight = 36;

    private readonly HashSet<string> _openFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<TileGroupVm> _groups = new();
    private readonly IntPtr _hwnd;
    private bool _recentExpanded, _tilesLoaded, _visible, _closing;
    private long _shownAt;
    private string? _filter;

    // live tiles: one 1 s clock while the menu is visible (stopped when hidden — zero cost otherwise)
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _liveTick;

    // tile drag state
    private TileVm? _dragTile;
    private Border? _dragBorder;
    private Point _dragStart, _grabOffset;
    private bool _dragging;

    public StartMenuWindow()
    {
        InitializeComponent();
        TileGroupsControl.ItemsSource = _groups;

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // tool window → never in Alt+Tab
        Native.SetWindowLongW(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLongW(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        IconLoader.SetDpiScale(VisualTreeHelper.GetDpi(this).DpiScaleX);

        LoadUserPicture();

        App.Catalog.Changed += OnCatalogChanged;
        Theme.Current.Changed += ApplyBackdrop;
        _liveTimer.Tick += (_, _) => LiveTick();
        ApplyBackdrop();
        BuildRail();
        ApplySettings();
    }

    /// <summary>Fills the rail's user button from the chosen (or Windows) account picture.</summary>
    private void LoadUserPicture()
    {
        UserNameText.Text = UserInfo.DisplayName;
        UserButton.ToolTip = UserNameText.Text;
        UserPicture.Visibility = Visibility.Collapsed;
        UserGlyph.Visibility = Visibility.Visible;
        if (UserInfo.PicturePath is not { } pic) return;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(pic);
            b.DecodePixelWidth = 64;
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            b.Freeze();
            UserPictureBrush.ImageSource = b;
            UserPicture.Visibility = Visibility.Visible;
            UserGlyph.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    // ───────────────────────── show / hide ─────────────────────────

    public void ToggleMenu()
    {
        if (_visible && !_closing) HideMenu(); else ShowMenu();
    }

    public void ShowMenu()
    {
        bool wasClosing = _closing;
        _closing = false;
        _shownAt = Environment.TickCount64;
        if (!_visible)
        {
            _visible = true;
            Position();
            Show();
            Animate(0, 1, 24, 0, 200);
            LiveTick(force: true);          // open with live faces already up
            _liveTimer.Start();
            App.Catalog.RefreshWebAssetsAsync();   // refresh website favicons / previews in the background
        }
        else if (wasClosing) Animate(Root.Opacity, 1, Slide.Y, 0, 120);   // caught mid fade-out
        Native.ForceForeground(_hwnd);
        Activate();
        AppList.Focus();
        StartHook.Debug($"ShowMenu visible={_visible} fg-after={(Native.GetForegroundWindow() == _hwnd)}");
    }

    public void HideMenu()
    {
        if (!_visible || _closing) return;
        _closing = true;
        PowerFlyout.IsOpen = UserFlyout.IsOpen = false;
        Animate(1, 0, 0, 8, 110, () =>
        {
            if (!_closing) return;                 // reopened during the fade
            _closing = false;
            _visible = false;
            _liveTimer.Stop();
            Hide();
            ResetTransient();
            if (App.Settings.TrimMemoryWhenHidden)
                Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        });
    }

    private void Animate(double fromOpacity, double toOpacity, double fromY, double toY, int ms, Action? done = null)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        var slide = new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (done != null) fade.Completed += (_, _) => done();
        Root.BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>Bottom-left above the taskbar (or wherever the taskbar is), in physical pixels → DIPs.</summary>
    private void Position()
    {
        var (tb, edge) = TaskbarInfo.GetTaskbar();
        var mon = TaskbarInfo.MonitorInfo(Native.MonitorFromPoint(
            new Native.POINT { X = (tb.Left + tb.Right) / 2, Y = (tb.Top + tb.Bottom) / 2 }, Native.MONITOR_DEFAULTTONEAREST));
        double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
        var work = mon.rcWork;

        int left = work.Left, top = Math.Min(tb.Top, work.Bottom) - h;
        switch (edge)
        {
            case Native.ABE_TOP: top = Math.Max(tb.Bottom, work.Top); break;
            case Native.ABE_LEFT: left = Math.Max(tb.Right, work.Left); top = work.Bottom - h; break;
            case Native.ABE_RIGHT: left = Math.Min(tb.Left, work.Right) - w; top = work.Bottom - h; break;
        }
        // Open where the Start button actually is: centred on it. A centred taskbar puts the menu in
        // the middle, a left-aligned one pushes it against the left edge once clamped below.
        if (!App.Settings.OpenAtCorner && edge is Native.ABE_TOP or Native.ABE_BOTTOM && TaskbarInfo.StartButton is { } sb)
            left = sb.Left + sb.Width / 2 - w / 2;

        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));

        left = Math.Clamp(left, mon.rcMonitor.Left, Math.Max(mon.rcMonitor.Left, mon.rcMonitor.Right - w));
        top = Math.Clamp(top, mon.rcMonitor.Top, Math.Max(mon.rcMonitor.Top, mon.rcMonitor.Bottom - h));
        Left = left / scale;
        Top = top / scale;
    }

    private void ResetTransient()
    {
        SearchBox.Text = string.Empty;
        JumpGrid.Visibility = Visibility.Collapsed;
        RailFlyout.Visibility = Visibility.Collapsed;
        if (FindChild<ScrollViewer>(AppList) is { } sv) sv.ScrollToTop();
        RebuildRows();   // "Most used" may have changed
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_visible || _closing) return;   // already hiding (e.g. launched an app) → let it hide
        // Ignore a deactivation in the first moments after showing: opening our window makes the
        // Windows 11 menu (or the previously-focused app) briefly churn the foreground, and we must
        // not hide ourselves in that window. We re-assert the foreground instead.
        if (Environment.TickCount64 - _shownAt < 400)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_visible && !_closing && !IsActive) { Native.ForceForeground(_hwnd); Activate(); }
            }));
            StartHook.Debug("Deactivated (grace) → re-assert");
            return;
        }
        // Our own popups/context menus briefly take the foreground; only hide for foreign windows.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_visible || IsActive) return;
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (fg != IntPtr.Zero && pid == Environment.ProcessId && !IsOtherWindowOfOurs(fg)) return;
            StartHook.Debug("Deactivated → hide");
            HideMenu();
        }));
    }

    private static bool IsOtherWindowOfOurs(IntPtr hwnd)
    {
        foreach (Window w in Application.Current.Windows)
            if (w is not StartMenuWindow && new WindowInteropHelper(w).Handle == hwnd) return true;
        return false;
    }

    // Window width = rail(48) + list(256) + chrome(26) + tile area(cols*Pitch - Gap).
    private const double ChromeWidth = 48 + 256 + 26 - TileVm.Gap;   // constant part; add cols*Pitch
    private static double WidthForColumns(int cols) => ChromeWidth + cols * TileVm.Pitch;
    private static int ColumnsForWidth(double width) => Math.Clamp((int)Math.Round((width - ChromeWidth) / TileVm.Pitch), 4, 12);

    public void ApplySettings()
    {
        var s = App.Settings;
        Height = s.MenuHeight;
        Width = WidthForColumns(s.TileColumns);
        foreach (var g in _groups) { g.Columns = s.TileColumns; g.Pack(); }
        BuildRail();
        LoadUserPicture();
        RebuildRows();
    }

    // ───────────────────────── resize grips ─────────────────────────
    // The menu is anchored bottom-left and grows up/right. We resize from the *absolute* cursor
    // position (not accumulated Thumb deltas): the grips sit on the moving edges, so a delta-based
    // approach makes the grip chase the cursor and under-reports movement — which left width stuck.

    private double _dragScale, _dragBottom, _dragLeft, _dragMaxH;

    private void Grip_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _dragScale = Native.GetDpiForWindow(_hwnd) / 96.0; if (_dragScale <= 0) _dragScale = 1;
        _dragBottom = Top + Height;
        _dragLeft = Left;
        var mon = TaskbarInfo.MonitorInfo(Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST));
        _dragMaxH = mon.rcWork.Height / _dragScale;
    }

    private void RightGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) => ApplyResize(true, false);
    private void TopGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) => ApplyResize(false, true);
    private void CornerGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) => ApplyResize(true, true);

    /// <summary>Live resize from the cursor: width snaps to whole tile columns (reflowing every group),
    /// height is free. The bottom-left corner stays pinned above the taskbar.</summary>
    private void ApplyResize(bool width, bool height)
    {
        Native.GetCursorPos(out var p);
        double cursorX = p.X / _dragScale, cursorY = p.Y / _dragScale;   // physical px → DIPs
        if (width)
        {
            int cols = ColumnsForWidth(cursorX - _dragLeft);
            if (cols != App.Settings.TileColumns)
            {
                App.Settings.TileColumns = cols;
                foreach (var g in _groups) { g.Columns = cols; g.Pack(); }
            }
            Width = WidthForColumns(cols);
        }
        if (height)
        {
            double h = Math.Clamp(_dragBottom - cursorY, 480, Math.Max(480, _dragMaxH));
            Height = h;
            Top = _dragBottom - h;                 // keep the bottom edge pinned above the taskbar
        }
    }

    private void Grip_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        App.Settings.MenuHeight = (int)Math.Round(Height);
        Store.Save("settings.json", App.Settings, JsonCtx.Default.Settings);
        StartHook.Debug($"resize → cols={App.Settings.TileColumns} h={App.Settings.MenuHeight}");
    }

    private void ApplyBackdrop()
    {
        var t = Theme.Current;
        bool blur = Backdrop.Apply(_hwnd, t.Tint, t.Transparency);
        // alpha 1 keeps the surface hit-testable; DWM paints the tinted blur underneath
        Root.Background = blur
            ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            : new SolidColorBrush(Color.FromRgb(t.Tint.R, t.Tint.G, t.Tint.B));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        IconLoader.SetDpiScale(newDpi.DpiScaleX);
        foreach (var a in App.Catalog.Apps) a.DropIcons();
    }

    // ───────────────────────── data ─────────────────────────

    private void OnCatalogChanged()
    {
        var catalog = App.Catalog;
        TileLayout layout;
        if (_tilesLoaded) layout = TileBoard.Save(_groups);
        else if (File.Exists(Path.Combine(Store.Dir, "tiles.json"))) layout = Store.Load("tiles.json", JsonCtx.Default.TileLayout);
        else layout = TileBoard.Default(catalog);
        _tilesLoaded = true;

        _groups.Clear();
        foreach (var g in TileBoard.Load(layout, catalog, App.Settings.TileColumns)) _groups.Add(g);
        RebuildRows();
    }

    private void RebuildRows()
    {
        var pinned = new HashSet<string>(_groups.SelectMany(g => g.Tiles).Select(t => t.App.Id), StringComparer.OrdinalIgnoreCase);
        AppList.ItemsSource = RowBuilder.Build(App.Catalog.Apps, App.Settings, pinned, _openFolders, _recentExpanded, _filter);
    }

    private void SaveTiles() => Store.Save("tiles.json", TileBoard.Save(_groups), JsonCtx.Default.TileLayout);

    private void LaunchApp(AppEntry app)
    {
        HideMenu();
        Launcher.Launch(app);
        App.Catalog.RecordLaunch(app);
    }

    private void AddTile(AppEntry app)
    {
        var group = _groups.LastOrDefault();
        if (group == null) _groups.Add(group = new TileGroupVm("Pinned", App.Settings.TileColumns));
        group.Add(new TileVm(app, TileSize.Medium, 0, -1));    // row -1 = "first free slot"
        SaveTiles();
    }

    private void RemoveEmptyGroups()
    {
        for (int i = _groups.Count - 1; i >= 0; i--)
            if (_groups[i].Tiles.Count == 0 && _groups.Count > 1) _groups.RemoveAt(i);
    }

    // ───────────────────────── keyboard / search ─────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // while a group name is being edited, Esc belongs to that box (revert), not to the menu
            case Key.Escape when Keyboard.FocusedElement is TextBox tb && tb != SearchBox:
                break;
            case Key.Escape:
                if (JumpGrid.Visibility == Visibility.Visible) JumpGrid.Visibility = Visibility.Collapsed;
                else if (RailFlyout.Visibility == Visibility.Visible) RailFlyout.Visibility = Visibility.Collapsed;
                else if (_filter != null) SearchBox.Text = string.Empty;
                else HideMenu();
                e.Handled = true;
                break;
            case Key.Enter when SearchBox.IsKeyboardFocused:
                if (AppList.Items.Count > 0 && AppList.Items[0] is AppRow first) LaunchApp(first.App);
                e.Handled = true;
                break;
            case Key.Down when SearchBox.IsKeyboardFocused:
                FocusRow(0);
                e.Handled = true;
                break;
            case Key.Back when _filter != null && !SearchBox.IsKeyboardFocused && Keyboard.FocusedElement is not TextBox:
                SearchBox.Text = SearchBox.Text[..^1];
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (_filter == null && char.IsWhiteSpace(e.Text[0])) return;   // Space activates rows, it does not start a search
        if (Keyboard.FocusedElement is TextBox) return;        // search box or a group name being edited
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Text += e.Text;
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = string.IsNullOrEmpty(SearchBox.Text) ? null : SearchBox.Text;
        if (_filter == null)
        {
            SearchBar.Visibility = Visibility.Collapsed;
            if (_visible) AppList.Focus();
        }
        else JumpGrid.Visibility = Visibility.Collapsed;
        RebuildRows();
    }

    private void AppList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is ListBoxItem { DataContext: ListRow row }) { ActivateRow(row); e.Handled = true; }
    }

    private void FocusRow(int index)
    {
        for (int i = index; i < AppList.Items.Count; i++)
        {
            if (AppList.Items[i] is not ListRow { Focusable: true } row) continue;
            AppList.ScrollIntoView(row);
            AppList.UpdateLayout();
            (AppList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem)?.Focus();
            return;
        }
    }

    private void ActivateRow(ListRow row)
    {
        switch (row)
        {
            case AppRow a: LaunchApp(a.App); break;
            case FolderRow f:
                if (!_openFolders.Remove(f.Name)) _openFolders.Add(f.Name);
                RebuildRows();
                break;
            case ExpandRow: _recentExpanded = !_recentExpanded; RebuildRows(); break;
            case HeaderRow { IsLetter: true }: ShowJumpGrid(); break;
        }
    }

    // ───────────────────────── app list rows ─────────────────────────

    private static T? Ctx<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    private void Header_Click(object sender, MouseButtonEventArgs e) { if (Ctx<HeaderRow>(sender) is { } h) ActivateRow(h); }
    private void App_Click(object sender, MouseButtonEventArgs e) { if (Ctx<AppRow>(sender) is { } r) ActivateRow(r); }
    private void Folder_Click(object sender, MouseButtonEventArgs e) { if (Ctx<FolderRow>(sender) is { } r) ActivateRow(r); }
    private void Expand_Click(object sender, MouseButtonEventArgs e) { if (Ctx<ExpandRow>(sender) is { } r) ActivateRow(r); }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<AppRow>(sender) is not { } row) return;
        if (row.IsPinned)
        {
            foreach (var g in _groups)
                foreach (var t in g.Tiles.Where(t => t.App.Id == row.App.Id).ToList()) g.Remove(t);
            RemoveEmptyGroups();
            SaveTiles();
        }
        else AddTile(row.App);
        RebuildRows();
    }

    private void RunAsAdmin_Click(object sender, RoutedEventArgs e) { if (Ctx<AppRow>(sender) is { } r) { HideMenu(); Launcher.RunAsAdmin(r.App); } }
    private void OpenLocation_Click(object sender, RoutedEventArgs e) { if (Ctx<AppRow>(sender) is { } r) { HideMenu(); Launcher.OpenFileLocation(r.App); } }
    private void Uninstall_Click(object sender, RoutedEventArgs e) { if (Ctx<AppRow>(sender) is { } r) { HideMenu(); Launcher.Uninstall(r.App); } }

    // ───────────────────────── alphabet jump grid ─────────────────────────

    private void ShowJumpGrid()
    {
        var present = new HashSet<string>(AppList.Items.OfType<HeaderRow>().Where(h => h.IsLetter).Select(h => h.Text));
        var letters = new List<string> { "#" };
        for (char c = 'A'; c <= 'Z'; c++) letters.Add(c.ToString());
        letters.Add("&");
        foreach (var extra in present.Where(p => !letters.Contains(p)).OrderBy(p => p, StringComparer.CurrentCulture)) letters.Add(extra);

        var style = (Style)FindResource("JumpCell");
        JumpPanel.Children.Clear();
        foreach (var l in letters)
        {
            var b = new Button { Content = l, Style = style, IsEnabled = present.Contains(l), Tag = l };
            b.Click += Jump_Click;
            JumpPanel.Children.Add(b);
        }
        JumpGrid.Visibility = Visibility.Visible;
    }

    private void Jump_Click(object sender, RoutedEventArgs e)
    {
        JumpGrid.Visibility = Visibility.Collapsed;
        var letter = (string)((Button)sender).Tag;
        for (int i = 0; i < AppList.Items.Count; i++)
        {
            if (AppList.Items[i] is not HeaderRow { IsLetter: true } h || h.Text != letter) continue;
            if (FindChild<ScrollViewer>(AppList) is { } sv) sv.ScrollToVerticalOffset(i * RowHeight);   // every row is 36 px
            return;
        }
    }

    // ───────────────────────── tiles ─────────────────────────

    // The transform declared in the tile DataTemplate is frozen by WPF's template optimisation, so we
    // give each tile its own mutable TransformGroup the first time it is touched.
    private static TransformGroup EnsureTransform(Border b)
    {
        if (b.RenderTransform is TransformGroup { IsFrozen: false } g && g.Children.Count == 2) return g;
        g = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.RenderTransform = g;
        return g;
    }
    private static ScaleTransform Scale(Border b) => (ScaleTransform)EnsureTransform(b).Children[0];
    private static TranslateTransform Translate(Border b) => (TranslateTransform)EnsureTransform(b).Children[1];

    // ───────────────────────── tile drag (smooth, Windows 10 style) ─────────────────────────

    private ItemsControl? _dragHost;          // canvas of the group the drag started in
    private double _dragBaseX, _dragBaseY;    // the tile's layout cell when the drag began

    private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.DataContext is not TileVm t) return;
        _dragBorder = b; _dragTile = t; _dragging = false;
        _dragStart = e.GetPosition(this);
        _grabOffset = e.GetPosition(b);
        Scale(b).ScaleX = Scale(b).ScaleY = 0.97;      // Windows 10 press effect
        b.CaptureMouse();
        e.Handled = true;
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBorder == null || _dragTile == null || e.LeftButton != MouseButtonState.Pressed) return;
        var t = _dragTile;
        var d = e.GetPosition(this) - _dragStart;
        if (!_dragging)
        {
            if (Math.Abs(d.X) <= 6 && Math.Abs(d.Y) <= 6) return;
            _dragging = true;
            t.IsDragging = true;
            t.ShowLive = false;
            _dragHost = HostOf(t.Group);
            _dragBaseX = t.X; _dragBaseY = t.Y;
            Scale(_dragBorder).ScaleX = Scale(_dragBorder).ScaleY = 1.05;   // lift
        }

        // Live preview: while over the source group, park the tile in the cell under the pointer so
        // the other tiles glide out of the way — the reflow the user sees before letting go.
        if (_dragHost != null)
        {
            var pos = e.GetPosition(this);
            var local = TranslatePoint(new Point(pos.X - _grabOffset.X, pos.Y - _grabOffset.Y), _dragHost);
            bool inside = local.X > -TileVm.Pitch && local.X < _dragHost.ActualWidth + TileVm.Pitch
                       && local.Y > -TileVm.Pitch && local.Y < _dragHost.ActualHeight + TileVm.Pitch;
            int col = Math.Clamp((int)Math.Round(local.X / TileVm.Pitch), 0, Math.Max(0, t.Group.Columns - t.Cols));
            int row = Math.Max(0, (int)Math.Round(local.Y / TileVm.Pitch));
            if (inside && (col != t.Col || row != t.Row))
            {
                t.Col = col; t.Row = row;
                AnimatedPack(t.Group, priority: t, exclude: t);
            }
        }
        // Keep the dragged tile under the pointer even though its layout cell may just have moved.
        var tt = Translate(_dragBorder);
        tt.X = d.X - (t.X - _dragBaseX);
        tt.Y = d.Y - (t.Y - _dragBaseY);
    }

    private void Tile_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragBorder == null || _dragTile == null) return;
        var b = _dragBorder; var t = _dragTile;
        _dragBorder = null; _dragTile = null; _dragHost = null;
        b.ReleaseMouseCapture();
        Scale(b).ScaleX = Scale(b).ScaleY = 1;
        e.Handled = true;

        var tt = Translate(b);
        if (!_dragging) { tt.X = 0; tt.Y = 0; LaunchApp(t.App); return; }
        _dragging = false;
        t.IsDragging = false;
        var pos = e.GetPosition(this);
        DropTile(t, new Point(pos.X - _grabOffset.X, pos.Y - _grabOffset.Y));

        // ease from under the pointer into the cell instead of snapping
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.X, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tt.Y, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        tt.X = 0; tt.Y = 0;
    }

    private void DropTile(TileVm tile, Point topLeft)
    {
        var centre = new Point(topLeft.X + tile.Width / 2, topLeft.Y + tile.Height / 2);
        TileGroupVm? target = null;
        ItemsControl? host = null;
        foreach (var g in _groups)
        {
            if (HostOf(g) is not { } ic) continue;
            var rect = new Rect(ic.TranslatePoint(new Point(0, 0), this), new Size(ic.ActualWidth, Math.Max(ic.ActualHeight, 1)));
            rect.Inflate(0, TileVm.Pitch);
            if (rect.Contains(centre)) { target = g; host = ic; break; }
        }

        var source = tile.Group;
        if (target == null || host == null)
        {
            // Windows 10 behaviour: dropping a tile below every group starts a NEW group.
            // Dropping anywhere else that isn't a group just glides the tile back.
            if (centre.Y > BottomOfLastGroup()) StartNewGroupWith(tile, source);
            else AnimatedPack(source);
            return;
        }

        var local = TranslatePoint(topLeft, host);
        if (target != source)
        {
            source.Tiles.Remove(tile);
            tile.Group = target;
            target.Tiles.Add(tile);
        }
        tile.Col = (int)Math.Round(local.X / TileVm.Pitch);
        tile.Row = Math.Max(0, (int)Math.Round(local.Y / TileVm.Pitch));
        AnimatedPack(target, priority: tile, exclude: target == source ? tile : null);
        if (target != source) AnimatedPack(source);
        RemoveEmptyGroups();
        SaveTiles();
    }

    private ItemsControl? HostOf(TileGroupVm g) =>
        TileGroupsControl.ItemContainerGenerator.ContainerFromItem(g) is ContentPresenter cp ? FindChild<ItemsControl>(cp) : null;

    /// <summary>Pack a group, then glide every tile that changed cell into its new place (Windows 10 reflow).</summary>
    private void AnimatedPack(TileGroupVm g, TileVm? priority = null, TileVm? exclude = null)
    {
        var host = HostOf(g);
        var before = new Dictionary<TileVm, (double x, double y)>();
        if (host != null)
            foreach (var t in g.Tiles)
                if (host.ItemContainerGenerator.ContainerFromItem(t) is ContentPresenter cp)
                    before[t] = (Canvas.GetLeft(cp), Canvas.GetTop(cp));
        g.Pack(priority);
        if (host == null) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var t in g.Tiles)
        {
            if (t == exclude || !before.TryGetValue(t, out var old)) continue;
            if (double.IsNaN(old.x) || (Math.Abs(old.x - t.X) < 0.5 && Math.Abs(old.y - t.Y) < 0.5)) continue;
            if (host.ItemContainerGenerator.ContainerFromItem(t) is not ContentPresenter cp) continue;
            cp.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(old.x, t.X, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            cp.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(old.y, t.Y, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }

    /// <summary>Y (window coords) of the bottom of the last group's tile canvas.</summary>
    private double BottomOfLastGroup()
    {
        double bottom = 0;
        foreach (var g in _groups)
        {
            if (TileGroupsControl.ItemContainerGenerator.ContainerFromItem(g) is not ContentPresenter cp) continue;
            if (FindChild<ItemsControl>(cp) is not { } ic) continue;
            double y = ic.TranslatePoint(new Point(0, ic.ActualHeight), this).Y;
            if (y > bottom) bottom = y;
        }
        return bottom;
    }

    /// <summary>Move a tile into a brand-new, unnamed group at the end of the board.</summary>
    private void StartNewGroupWith(TileVm tile, TileGroupVm source)
    {
        var group = new TileGroupVm(string.Empty, App.Settings.TileColumns);
        _groups.Add(group);
        source.Tiles.Remove(tile);
        tile.Group = group;
        tile.Col = 0;
        tile.Row = 0;
        group.Tiles.Add(tile);
        group.Pack();
        source.Pack();
        RemoveEmptyGroups();
        SaveTiles();
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is not { } t) return;
        var grp = t.Group; grp.Tiles.Remove(t); AnimatedPack(grp);
        RemoveEmptyGroups();
        SaveTiles();
        RebuildRows();
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is not { } t || sender is not MenuItem { Tag: string tag }) return;
        t.Size = Enum.Parse<TileSize>(tag);
        AnimatedPack(t.Group, priority: t);
        SaveTiles();
    }

    private void LiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is not { } t) return;
        t.Live = !t.Live;
        if (t.Live) { RefreshLive(t, DateTime.Now, newPhoto: true); t.ShowLive = true; }
        SaveTiles();
    }

    private void TileRunAsAdmin_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.RunAsAdmin(t.App); } }
    private void TileOpenLocation_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.OpenFileLocation(t.App); } }
    private void TileUninstall_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.Uninstall(t.App); } }

    private void GroupName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (tb.DataContext is TileGroupVm g) g.IsEditing = false;
        SaveTiles();
    }

    // ── group header: click renames, drag moves the whole group ──

    private TileGroupVm? _headerPress;
    private Point _headerStart;
    private bool _headerDragging;

    private void GroupHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TileGroupVm g } fe) return;
        _headerPress = g;
        _headerDragging = false;
        _headerStart = e.GetPosition(this);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void GroupHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (_headerPress == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        var d = pos - _headerStart;
        if (!_headerDragging && (Math.Abs(d.X) > 6 || Math.Abs(d.Y) > 6)) _headerDragging = true;
        if (!_headerDragging) return;
        // show where the group will land
        DropIndexFor(pos.Y, out double lineY);
        GroupInsertLine.Margin = new Thickness(12, Math.Max(0, lineY), 12, 0);
        GroupInsertLine.Visibility = Visibility.Visible;
    }

    private void GroupHeader_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || _headerPress is not { } group) return;
        fe.ReleaseMouseCapture();
        var dropped = e.GetPosition(this);
        _headerPress = null;
        GroupInsertLine.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (!_headerDragging)
        {
            // plain click → rename, and put the caret in the box that just appeared
            group.IsEditing = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (FindSibling<TextBox>(fe) is not { } box) return;
                box.Focus();
                box.SelectAll();
            }));
            return;
        }

        _headerDragging = false;
        MoveGroupTo(group, dropped.Y);
    }

    /// <summary>Index a group dropped at window-Y should take, and the Y of the indicator line.</summary>
    private int DropIndexFor(double y, out double lineY)
    {
        lineY = 0;
        for (int i = 0; i < _groups.Count; i++)
        {
            if (TileGroupsControl.ItemContainerGenerator.ContainerFromItem(_groups[i]) is not ContentPresenter cp) continue;
            double top = cp.TranslatePoint(new Point(0, 0), this).Y;
            if (y < top + cp.ActualHeight / 2) { lineY = top - 6; return i; }
            lineY = top + cp.ActualHeight - 6;
        }
        return _groups.Count;
    }

    /// <summary>Reorder a dragged group to wherever it was dropped vertically.</summary>
    private void MoveGroupTo(TileGroupVm group, double y)
    {
        int from = _groups.IndexOf(group);
        int target = DropIndexFor(y, out _);
        if (target > from) target--;                       // removing it first shifts the later indices
        target = Math.Clamp(target, 0, _groups.Count - 1);
        if (from < 0 || from == target) return;
        _groups.Move(from, target);
        SaveTiles();
    }

    private static T? FindSibling<T>(FrameworkElement from) where T : DependencyObject
    {
        if (VisualTreeHelper.GetParent(from) is not { } parent) return null;
        int n = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
            if (VisualTreeHelper.GetChild(parent, i) is T t) return t;
        return null;
    }
    private void GroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var binding = tb.GetBindingExpression(TextBox.TextProperty);
        if (e.Key == Key.Enter) { binding?.UpdateSource(); SaveTiles(); }
        else if (e.Key == Key.Escape) binding?.UpdateTarget();
        else return;
        if (tb.DataContext is TileGroupVm g) g.IsEditing = false;
        AppList.Focus();
        e.Handled = true;
    }

    // ───────────────────────── board context menu ─────────────────────────

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add a program or file to Start",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.msc|All files|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        AddCustomTile(Path.GetFileNameWithoutExtension(dlg.FileName), dlg.FileName);
    }

    private void AddWebsite_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        var w = new InputWindow();
        if (w.ShowDialog() != true) return;
        AddCustomTile(w.ResultName, w.ResultValue);
    }

    /// <summary>Registers a user-added target and pins a tile for it.</summary>
    private void AddCustomTile(string name, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var entry = App.Catalog.AddCustom(string.IsNullOrWhiteSpace(name) ? target : name, target);
        AddTile(entry);
        RebuildRows();
    }

    private void TileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is { } t) EditCustom(t.App);
    }

    private void RowEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<AppRow>(sender) is { } r) EditCustom(r.App);
    }

    /// <summary>Rename / retarget a user-added website, program or file.</summary>
    private void EditCustom(AppEntry app)
    {
        if (!app.IsCustom) return;
        HideMenu();
        var w = new InputWindow(app.Name, app.CustomTarget);
        if (w.ShowDialog() != true) return;
        App.Catalog.UpdateCustom(app.Id, w.ResultName, w.ResultValue);
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        ((App)Application.Current).ShowSettings();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        new AboutWindow().Show();
    }

    // ───────────────────────── left rail (configurable) ─────────────────────────


    /// <summary>Rebuilds the rail's middle section from Settings.Rail.</summary>
    private void BuildRail()
    {
        var r = App.Settings.Rail;
        var items = new List<RailItemVm>();
        void Add(bool on, string glyph, string tip, Action act)
        {
            if (on) items.Add(new RailItemVm { Glyph = glyph, Tooltip = tip, Invoke = act });
        }
        void Open(Action a) { HideMenu(); a(); }

        Add(r.Documents, "", "Documents", () => Open(Launcher.OpenDocuments));
        Add(r.Downloads, "", "Downloads", () => Open(Launcher.OpenDownloads));
        Add(r.Music, "", "Music", () => Open(Launcher.OpenMusic));
        Add(r.Pictures, "", "Pictures", () => Open(Launcher.OpenPictures));
        Add(r.Videos, "", "Videos", () => Open(Launcher.OpenVideos));
        Add(r.Network, "", "Network", () => Open(Launcher.OpenNetwork));
        Add(r.PersonalFolder, "", "Personal folder", () => Open(Launcher.OpenPersonalFolder));
        Add(r.FileExplorer, "", "File Explorer", () => Open(Launcher.OpenFileExplorer));
        Add(r.Settings, "", "Settings", () => Open(Launcher.OpenSettings));
        Add(r.Calendar, "", "Calendar", ShowCalendar);
        RailList.ItemsSource = items;
        RailFlyoutList.ItemsSource = items;   // the hamburger view shows exactly the same entries
    }

    private void RailItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RailItemVm item) item.Invoke();
    }

    private void ShowCalendar()
    {
        HideMenu();
        ((App)Application.Current).ShowCalendar();
    }

    // ───────────────────────── left rail ─────────────────────────

    private void Hamburger_Click(object sender, RoutedEventArgs e) =>
        RailFlyout.Visibility = RailFlyout.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void User_Click(object sender, RoutedEventArgs e) => UserFlyout.IsOpen = true;
    private void Power_Click(object sender, RoutedEventArgs e) => PowerFlyout.IsOpen = true;
    private void Documents_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.OpenDocuments(); }
    private void Pictures_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.OpenPictures(); }
    private void Settings_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.OpenSettings(); }

    private void Sleep_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.Sleep(); }
    private void ShutDown_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.ShutDown(); }
    private void Restart_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.Restart(); }
    private void AccountSettings_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.OpenAccountSettings(); }
    private void Lock_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.Lock(); }
    private void SignOut_Click(object sender, RoutedEventArgs e) { HideMenu(); Launcher.SignOut(); }

    // ───────────────────────── live tiles ─────────────────────────

    /// <summary>Each live tile shows its face ~9 s then the icon ~3 s, staggered per tile; the clock
    /// face is refreshed every second while up; Photos gets a new picture each time it comes up.</summary>
    private void LiveTick(bool force = false)
    {
        _liveTick++;
        var now = DateTime.Now;
        foreach (var t in _groups.SelectMany(g => g.Tiles))
        {
            if (!t.CanBeLive || !t.Live) { t.ShowLive = false; continue; }
            if (t.IsDragging) continue;
            int cycle = (_liveTick + t.LivePhase * 2) % 12;
            bool wantLive = force || cycle < 9;
            bool comingUp = wantLive && !t.ShowLive;
            if (wantLive && (comingUp || t.Kind == LiveKind.Clock)) RefreshLive(t, now, newPhoto: comingUp);
            t.ShowLive = wantLive;
        }
    }

    private void RefreshLive(TileVm t, DateTime now, bool newPhoto)
    {
        switch (t.Kind)
        {
            case LiveKind.Calendar:
                var (title, big, sub) = LiveTiles.CalendarFace(now);
                t.LiveTitle = title; t.LiveBig = big; t.LiveSub = sub; t.LiveImage = null;
                break;
            case LiveKind.Clock:
                var (cb, cs) = LiveTiles.ClockFace(now);
                t.LiveTitle = string.Empty; t.LiveBig = cb; t.LiveSub = cs; t.LiveImage = null;
                break;
            case LiveKind.Website:
                if (t.App.ThumbPath is { } thumb && File.Exists(thumb))
                {
                    int w = (int)Math.Ceiling(t.Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
                    Task.Run(() => LiveTiles.LoadImage(thumb, w)).ContinueWith(task => Dispatcher.BeginInvoke(() =>
                    {
                        if (task.Result is { } img) t.LiveImage = img;
                    }));
                }
                else
                {
                    t.LiveTitle = string.Empty; t.LiveBig = string.Empty; t.LiveImage = null;
                    t.LiveSub = Uri.TryCreate(t.App.CustomTarget, UriKind.Absolute, out var u) ? u.Host : t.App.Name;
                }
                break;
            case LiveKind.Photos:
                if (!newPhoto && t.LiveImage != null) break;
                int width = (int)Math.Ceiling(t.Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
                Task.Run(() => LiveTiles.NextPhoto(width)).ContinueWith(task => Dispatcher.BeginInvoke(() =>
                {
                    if (task.Result is { } img) t.LiveImage = img;
                    else { t.LiveTitle = string.Empty; t.LiveBig = string.Empty; t.LiveSub = "No pictures in your Pictures folder"; t.LiveImage = null; }
                }));
                break;
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } found) return found;
        }
        return null;
    }
}
