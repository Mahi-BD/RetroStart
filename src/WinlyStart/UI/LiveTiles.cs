using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using WinlyStart.Core;

namespace WinlyStart.UI;

/// <summary>Data behind the live-tile faces: next calendar note, the clock, and a Pictures slideshow.</summary>
internal static class LiveTiles
{
    private static CalendarData? _calendar;
    private static long _calendarStamp;
    private static List<string>? _photos;
    private static int _photoIndex;
    private static readonly Dictionary<string, BitmapSource> _photoCache = new();

    /// <summary>Day name, day number, and the next note on or after today (re-read at most once a minute).</summary>
    public static (string title, string big, string sub) CalendarFace(DateTime now)
    {
        if (_calendar == null || Environment.TickCount64 - _calendarStamp > 60_000)
        {
            _calendar = Store.Load("calendar.json", JsonCtx.Default.CalendarData);
            _calendarStamp = Environment.TickCount64;
        }
        string sub = "No upcoming notes";
        var today = now.Date;
        foreach (var (key, text) in _calendar.Notes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d < today) continue;
            string first = text.Split('\n')[0].Trim();
            sub = (d == today ? "Today" : d == today.AddDays(1) ? "Tomorrow" : d.ToString("d MMM", CultureInfo.CurrentCulture)) + " · " + first;
            break;
        }
        return (now.ToString("dddd", CultureInfo.CurrentCulture), now.Day.ToString(CultureInfo.CurrentCulture), sub);
    }

    public static (string big, string sub) ClockFace(DateTime now) =>
        (now.ToString("t", CultureInfo.CurrentCulture), now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture));

    /// <summary>Next picture from the user's Pictures folder (top level + one level down), decoded small and cached.</summary>
    private static readonly object _photoLock = new();

    public static BitmapSource? NextPhoto(int decodeWidth)
    {
        lock (_photoLock)
        try
        {
            if (_photos == null)
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var list = new List<string>();
                if (Directory.Exists(root))
                {
                    foreach (var dir in new[] { root }.Concat(Directory.EnumerateDirectories(root).Take(12)))
                    {
                        try
                        {
                            list.AddRange(Directory.EnumerateFiles(dir)
                                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                .Take(40));
                        }
                        catch { }
                        if (list.Count >= 60) break;
                    }
                }
                var rnd = new Random();
                _photos = list.OrderBy(_ => rnd.Next()).ToList();
            }
            if (_photos.Count == 0) return null;
            string path = _photos[_photoIndex++ % _photos.Count];
            if (_photoCache.TryGetValue(path, out var cached)) return cached;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = decodeWidth;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            if (_photoCache.Count > 16) _photoCache.Clear();
            _photoCache[path] = bi;
            return bi;
        }
        catch { return null; }
    }
}
