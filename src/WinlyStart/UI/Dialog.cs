using System.Windows;
using System.Windows.Interop;
using WinlyStart.Core;

namespace WinlyStart.UI;

/// <summary>Shared chrome for the dialog-style windows (Settings, About, Calendar…):
/// a dark title bar when Windows app mode is dark, and the app icon.</summary>
internal static class Dialog
{
    public static void Apply(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int dark = Theme.Current.IsAppLight ? 0 : 1;
            try { Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)); } catch { }
        };
    }
}
