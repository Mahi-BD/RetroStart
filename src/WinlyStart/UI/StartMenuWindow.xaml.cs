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

        UserNameText.Text = UserInfo.DisplayName;
        UserButton.ToolTip = UserNameText.Text;
        if (UserInfo.PicturePath is { } pic)
        {
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

        App.Catalog.Changed += OnCatalogChanged;
        Theme.Current.Changed += ApplyBackdrop;
        ApplyBackdrop();
        ApplySettings();
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
        if (App.Settings.OpenAtStartButton && edge is Native.ABE_TOP or Native.ABE_BOTTOM && TaskbarInfo.StartButton is { } sb)
            left = sb.Left;

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
        var d = e.GetPosition(this) - _dragStart;
        if (!_dragging && (Math.Abs(d.X) > 6 || Math.Abs(d.Y) > 6))
        {
            _dragging = true;
            _dragTile.IsDragging = true;
            Scale(_dragBorder).ScaleX = Scale(_dragBorder).ScaleY = 1;
        }
        if (_dragging) { var tt = Translate(_dragBorder); tt.X = d.X; tt.Y = d.Y; }
    }

    private void Tile_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragBorder == null || _dragTile == null) return;
        var b = _dragBorder; var t = _dragTile;
        _dragBorder = null; _dragTile = null;
        b.ReleaseMouseCapture();
        Scale(b).ScaleX = Scale(b).ScaleY = 1;
        var tt = Translate(b); tt.X = 0; tt.Y = 0;
        e.Handled = true;

        if (!_dragging) { LaunchApp(t.App); return; }
        _dragging = false;
        t.IsDragging = false;
        var pos = e.GetPosition(this);
        DropTile(t, new Point(pos.X - _grabOffset.X, pos.Y - _grabOffset.Y));
    }

    private void DropTile(TileVm tile, Point topLeft)
    {
        var centre = new Point(topLeft.X + tile.Width / 2, topLeft.Y + tile.Height / 2);
        TileGroupVm? target = null;
        ItemsControl? host = null;
        foreach (var g in _groups)
        {
            if (TileGroupsControl.ItemContainerGenerator.ContainerFromItem(g) is not ContentPresenter cp) continue;
            if (FindChild<ItemsControl>(cp) is not { } ic) continue;
            var rect = new Rect(ic.TranslatePoint(new Point(0, 0), this), new Size(ic.ActualWidth, Math.Max(ic.ActualHeight, 1)));
            rect.Inflate(0, TileVm.Pitch);
            if (rect.Contains(centre)) { target = g; host = ic; break; }
        }

        var source = tile.Group;
        if (target == null || host == null) { source.Pack(); return; }

        var local = TranslatePoint(topLeft, host);
        if (target != source)
        {
            source.Tiles.Remove(tile);
            tile.Group = target;
            target.Tiles.Add(tile);
        }
        tile.Col = (int)Math.Round(local.X / TileVm.Pitch);
        tile.Row = Math.Max(0, (int)Math.Round(local.Y / TileVm.Pitch));
        target.Pack(tile);
        if (target != source) source.Pack();
        RemoveEmptyGroups();
        SaveTiles();
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is not { } t) return;
        t.Group.Remove(t);
        RemoveEmptyGroups();
        SaveTiles();
        RebuildRows();
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (Ctx<TileVm>(sender) is not { } t || sender is not MenuItem { Tag: string tag }) return;
        t.Size = Enum.Parse<TileSize>(tag);
        t.Group.Pack(t);
        SaveTiles();
    }

    private void TileRunAsAdmin_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.RunAsAdmin(t.App); } }
    private void TileOpenLocation_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.OpenFileLocation(t.App); } }
    private void TileUninstall_Click(object sender, RoutedEventArgs e) { if (Ctx<TileVm>(sender) is { } t) { HideMenu(); Launcher.Uninstall(t.App); } }

    private void GroupName_LostFocus(object sender, RoutedEventArgs e)
    {
        (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        SaveTiles();
    }
    private void GroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) { AppList.Focus(); e.Handled = true; }
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
