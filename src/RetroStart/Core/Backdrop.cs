using System.Runtime.InteropServices;
using System.Windows.Media;

namespace RetroStart.Core;

/// <summary>
/// Windows 10 style acrylic: DWM blurs whatever is behind the window and paints our tint on top.
/// Uses the composition attribute that the Windows 10 Start menu itself is built on; works on
/// Windows 10 1803+ and Windows 11. Square corners are enforced so the menu looks like Windows 10.
/// </summary>
public static class Backdrop
{
    /// <returns>true when the OS accepted the blur; false → caller should paint a solid surface.</returns>
    public static bool Apply(IntPtr hwnd, Color tint, bool enabled)
    {
        try
        {
            int corner = Native.DWMWCP_DONOTROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch { /* pre-Windows 11 DWM ignores this */ }

        var policy = new Native.ACCENT_POLICY
        {
            AccentState = enabled ? Native.ACCENT_ENABLE_ACRYLICBLURBEHIND : Native.ACCENT_DISABLED,
            AccentFlags = 0,
            // ABGR, alpha in the top byte
            GradientColor = (uint)(tint.A << 24 | tint.B << 16 | tint.G << 8 | tint.R),
        };
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<Native.ACCENT_POLICY>());
        try
        {
            Marshal.StructureToPtr(policy, p, false);
            var data = new Native.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = Native.WCA_ACCENT_POLICY,
                Data = p,
                SizeOfData = Marshal.SizeOf<Native.ACCENT_POLICY>(),
            };
            return Native.SetWindowCompositionAttribute(hwnd, ref data) && enabled;
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(p); }
    }
}
