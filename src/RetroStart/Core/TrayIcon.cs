using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RetroStart.Core;

internal static partial class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string lpString);
}

/// <summary>Notification-area icon without WinForms: a message-only window + Shell_NotifyIcon.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_TRAY = Native.WM_APP + 1;
    private const int WM_CONTEXTMENU = 0x007B;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private Native.NOTIFYICONDATA _data;
    private IntPtr _icon;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(string tooltip)
    {
        _source = new HwndSource(new HwndSourceParameters("RetroStart.Tray")
        {
            Width = 0, Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ParentWindow = HWND_MESSAGE,
        });
        _source.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");

        if (Environment.ProcessPath is { } exe)
        {
            Native.ExtractIconExW(exe, 0, out var large, out var small, 1);
            _icon = small != IntPtr.Zero ? small : large;
            if (large != IntPtr.Zero && large != _icon) Native.DestroyIcon(large);
        }

        _data = new Native.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
            szTip = tooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        Add();
    }

    private void Add()
    {
        Native.Shell_NotifyIconW(Native.NIM_ADD, ref _data);
        _data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIconW(Native.NIM_SETVERSION, ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            int evt = (int)(lParam.ToInt64() & 0xFFFF);
            if (evt == Native.WM_LBUTTONUP) LeftClick?.Invoke();
            else if (evt == Native.WM_RBUTTONUP || evt == WM_CONTEXTMENU) RightClick?.Invoke();
            handled = true;
        }
        else if (msg == (int)_taskbarCreated) Add();   // explorer restarted → icon must be re-registered
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Native.Shell_NotifyIconW(Native.NIM_DELETE, ref _data);
        if (_icon != IntPtr.Zero) { Native.DestroyIcon(_icon); _icon = IntPtr.Zero; }
        _source.Dispose();
    }
}
