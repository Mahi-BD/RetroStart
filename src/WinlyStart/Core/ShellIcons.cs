using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinlyStart.Core;

/// <summary>
/// One icon pipeline for desktop shortcuts and packaged apps:
/// IShellItemImageFactory → HBITMAP → GetDIBits (top-down BGRA) → frozen BitmapSource.
/// No System.Drawing, no GDI+.
/// </summary>
public static class ShellIcons
{
    public static BitmapSource? Load(string parsingName, int pixels)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (Native.CreateShellItem(parsingName) is not Native.IShellItemImageFactory factory) return null;
            int hr = factory.GetImage(new Native.SIZE(pixels, pixels), Native.SIIGBF_ICONONLY | Native.SIIGBF_BIGGERSIZEOK, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
            {
                // some items refuse ICONONLY (e.g. .url with favicon) → take whatever the shell offers
                hr = factory.GetImage(new Native.SIZE(pixels, pixels), Native.SIIGBF_RESIZETOFIT, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
            }
            return FromHBitmap(hbm);
        }
        catch { return null; }
        finally { if (hbm != IntPtr.Zero) Native.DeleteObject(hbm); }
    }

    /// <summary>Decode a packed application resource (PNG) at the requested size.</summary>
    public static BitmapSource? LoadResource(string relativePath, int pixels)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri("pack://application:,,,/" + relativePath, UriKind.Absolute);
            bi.DecodePixelWidth = pixels;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (Native.GetObjectW(hbm, Marshal.SizeOf<Native.BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,          // negative = top-down rows, exactly what WPF wants
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Native.BI_RGB,
        };
        var pixels = new byte[w * h * 4];
        var dc = Native.GetDC(IntPtr.Zero);
        try
        {
            if (Native.GetDIBits(dc, hbm, 0, (uint)h, pixels, ref header, Native.DIB_RGB_COLORS) == 0) return null;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, dc); }

        // Legacy 24-bit icons (and a few 32-bit ones drawn without alpha) come back fully transparent.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        // GetDIBits returns STRAIGHT (non-premultiplied) alpha, so the format must be Bgra32.
        // Using Pbgra32 here made edge pixels blend wrong → the fringed/jagged icon borders.
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// Single background STA worker that resolves icons lazily, in the order the UI asks for them.
/// Keeps CPU spikes and the working set low even with hundreds of apps.
/// </summary>
public static class IconLoader
{
    private static readonly BlockingCollection<(AppEntry app, int size)> Queue = new();
    private static readonly ConcurrentDictionary<(string id, int size), bool> Pending = new();
    private static double _dpiScale = 1.0;

    static IconLoader()
    {
        var t = new Thread(Worker) { IsBackground = true, Name = "WinlyStart.Icons", Priority = ThreadPriority.BelowNormal };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    /// <summary>Called by the window whenever its DPI changes so icons are rendered crisp.</summary>
    public static void SetDpiScale(double scale) => _dpiScale = scale <= 0 ? 1.0 : scale;

    public static void Request(AppEntry app, int size)
    {
        if (Pending.TryAdd((app.Id, size), true)) Queue.Add((app, size));
    }

    private static void Worker()
    {
        foreach (var (app, size) in Queue.GetConsumingEnumerable())
        {
            int px = (int)Math.Round(size * _dpiScale);
            var bmp = app.ParsingName.StartsWith("res:", StringComparison.Ordinal)
                ? ShellIcons.LoadResource(app.ParsingName[4..], px)
                : ShellIcons.Load(app.ParsingName, px);
            Pending.TryRemove((app.Id, size), out _);
            if (bmp == null) continue;
            Application.Current?.Dispatcher.BeginInvoke(() => app.SetIcon(size, bmp));
        }
    }
}
