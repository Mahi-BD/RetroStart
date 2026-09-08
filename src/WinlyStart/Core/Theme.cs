using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinlyStart.Core;

/// <summary>
/// Reads the Windows 11 personalisation settings (light/dark, accent colour, accent on Start,
/// transparency) and publishes them as dynamic resource brushes styled the Windows 10 way.
/// </summary>
public sealed class Theme
{
    public static readonly Theme Current = new();

    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";

    public bool IsLight { get; private set; }
    /// <summary>Windows "app mode" (AppsUseLightTheme) — what our dialog windows follow.</summary>
    public bool IsAppLight { get; private set; } = true;
    public bool AccentOnStart { get; private set; }
    public bool Transparency { get; private set; } = true;
    public Color Accent { get; private set; } = Color.FromRgb(0, 120, 215);
    /// <summary>Tint painted over the acrylic blur (alpha included), or solid when transparency is off.</summary>
    public Color Tint { get; private set; }

    public event Action? Changed;
    private ThemeMode _mode;
    private bool _hooked;

    public void Start(ThemeMode mode)
    {
        _mode = mode;
        Refresh();
        if (_hooked) return;
        _hooked = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
                or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Window)
                Application.Current?.Dispatcher.BeginInvoke(Refresh);
        };
    }

    public void SetMode(ThemeMode mode) { _mode = mode; Refresh(); }

    public void Refresh()
    {
        int light = ReadDword(Personalize, "SystemUsesLightTheme", 0);
        IsLight = _mode switch { ThemeMode.Light => true, ThemeMode.Dark => false, _ => light == 1 };
        int appsLight = ReadDword(Personalize, "AppsUseLightTheme", 1);
        IsAppLight = _mode switch { ThemeMode.Light => true, ThemeMode.Dark => false, _ => appsLight == 1 };
        AccentOnStart = ReadDword(Personalize, "ColorPrevalence", 0) == 1;
        Transparency = ReadDword(Personalize, "EnableTransparency", 1) == 1;

        uint abgr = (uint)ReadDword(Dwm, "AccentColor", unchecked((int)0xFFD77800));
        Accent = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));

        byte alpha = Transparency ? (byte)0xCC : (byte)0xFF;
        Tint = AccentOnStart
            ? Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B)
            : IsLight ? Color.FromArgb(alpha, 0xF2, 0xF2, 0xF2) : Color.FromArgb(alpha, 0x1F, 0x1F, 0x1F);

        if (Application.Current != null) Apply(Application.Current.Resources);
        Changed?.Invoke();
    }

    private void Apply(ResourceDictionary r)
    {
        // With accent-on-Start the surface is coloured, so text must be light even in light mode.
        bool darkText = IsLight && !AccentOnStart;
        Color fg = darkText ? Colors.Black : Colors.White;

        r["Rs.Text"] = Brush(fg);
        r["Rs.TextSecondary"] = Brush(Color.FromArgb(0x99, fg.R, fg.G, fg.B));
        r["Rs.Hover"] = Brush(Color.FromArgb(0x19, fg.R, fg.G, fg.B));
        r["Rs.Pressed"] = Brush(Color.FromArgb(0x33, fg.R, fg.G, fg.B));
        r["Rs.Divider"] = Brush(Color.FromArgb(0x33, fg.R, fg.G, fg.B));
        r["Rs.Rail"] = Brush(Color.FromArgb(0x0F, fg.R, fg.G, fg.B));
        r["Rs.Flyout"] = Brush(darkText ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x2B, 0x2B, 0x2B));
        r["Rs.Accent"] = Brush(Accent);
        r["Rs.AccentText"] = Brush(Luminance(Accent) > 0.6 ? Colors.Black : Colors.White);
        r["Rs.Header"] = Brush(AccentOnStart ? fg : Accent);       // A–Z headers are accent coloured on a neutral surface
        r["Rs.TileBorder"] = Brush(Color.FromArgb(0x66, fg.R, fg.G, fg.B));
        r["Rs.TileBorderPressed"] = Brush(Color.FromArgb(0x99, fg.R, fg.G, fg.B));
        r["Rs.Search"] = Brush(darkText ? Colors.White : Color.FromRgb(0x2B, 0x2B, 0x2B));
        r["Rs.TintColor"] = Tint;

        // Dialog windows (Settings, About, Calendar…): Windows 11 app-mode palette, flat cards.
        bool dk = !IsAppLight;
        r["Dlg.Bg"] = Brush(dk ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        r["Dlg.Card"] = Brush(dk ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White);
        r["Dlg.Border"] = Brush(dk ? Color.FromRgb(0x3D, 0x3D, 0x3D) : Color.FromRgb(0xE3, 0xE3, 0xE3));
        r["Dlg.Text"] = Brush(dk ? Colors.White : Color.FromRgb(0x1B, 0x1B, 0x1B));
        r["Dlg.TextSecondary"] = Brush(dk ? Color.FromRgb(0xA6, 0xA6, 0xA6) : Color.FromRgb(0x61, 0x61, 0x61));
        r["Dlg.Control"] = Brush(dk ? Color.FromRgb(0x33, 0x33, 0x33) : Color.FromRgb(0xFB, 0xFB, 0xFB));
        r["Dlg.ControlBorder"] = Brush(dk ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(0xD0, 0xD0, 0xD0));
        r["Dlg.Hover"] = Brush(dk ? Color.FromRgb(0x38, 0x38, 0x38) : Color.FromRgb(0xEA, 0xEA, 0xEA));
        r["Dlg.Pressed"] = Brush(dk ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xDE, 0xDE, 0xDE));
        r["Dlg.Accent"] = Brush(Accent);
        r["Dlg.AccentHover"] = Brush(Color.FromRgb((byte)Math.Min(255, Accent.R + 24), (byte)Math.Min(255, Accent.G + 24), (byte)Math.Min(255, Accent.B + 24)));
        r["Dlg.AccentText"] = Brush(Luminance(Accent) > 0.6 ? Colors.Black : Colors.White);
        r["Dlg.SwitchOff"] = Brush(dk ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x86, 0x86, 0x86));
        // The window paints this itself only when the OS blur is unavailable; otherwise DWM paints the tint.
        r["Rs.Surface"] = Brush(Transparency ? Colors.Transparent : Color.FromRgb(Tint.R, Tint.G, Tint.B));
    }

    private static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static int ReadDword(string key, string name, int fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            return k?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }
}
