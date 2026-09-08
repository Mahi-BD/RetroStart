using System.Runtime.InteropServices;
using System.Windows;

namespace RetroStart.Core;

/// <summary>
/// The "replacement" — three user-mode, fully revertible hooks:
///  1. keyboard: a bare Windows-key tap (or Ctrl+Esc) toggles Retro Start instead of the Windows 11 menu,
///  2. mouse: a left-click on the taskbar's Start button toggles Retro Start,
///  3. foreground WinEvent: if the Windows 11 Start menu still appears (touch, elevated window focused),
///     Retro Start takes over and the Windows menu light-dismisses.
/// Nothing is injected anywhere; unhooking (or closing the app) restores Windows completely.
/// </summary>
public sealed class StartHook : IDisposable
{
    private static readonly UIntPtr Magic = new(0x52535452); // "RSTR" tags our own injected key
    private const ushort DummyVk = 0xE8;                       // unassigned virtual key, harmless

    private readonly Native.HookProc _kbProc;
    private readonly Native.HookProc _mouseProc;
    private readonly Native.WinEventProc _winEventProc;
    private IntPtr _kbHook, _mouseHook, _winEventHook;

    private bool _winDown, _otherKey, _swallowUp;
    private long _lastWinDown;
    private readonly Dictionary<uint, bool> _isStartHost = new();

    public bool WinKeyEnabled { get; set; } = true;
    public bool StartButtonEnabled { get; set; } = true;
    public bool FallbackEnabled { get; set; } = true;

    /// <summary>Raised on the UI thread. Toggle = open/close, Show = make sure it is open.</summary>
    public event Action? Toggle;
    public event Action? Show;

    public StartHook()
    {
        _kbProc = KeyboardProc;
        _mouseProc = MouseProc;
        _winEventProc = WinEventProc;
    }

    public void Install()
    {
        var module = Native.GetModuleHandleW(null);
        if (_kbHook == IntPtr.Zero) _kbHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _kbProc, module, 0);
        if (_mouseHook == IntPtr.Zero) _mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_winEventHook == IntPtr.Zero)
            _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    private IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !WinKeyEnabled) return Native.CallNextHookEx(_kbHook, code, wParam, lParam);

        var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if ((k.flags & Native.LLKHF_INJECTED) != 0 && k.dwExtraInfo == Magic)
            return Native.CallNextHookEx(_kbHook, code, wParam, lParam);   // our own dummy key

        int msg = (int)wParam;
        bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        bool isWin = k.vkCode is Native.VK_LWIN or Native.VK_RWIN;

        if (isWin)
        {
            long now = Environment.TickCount64;
            if (down)
            {
                // First event of a press (auto-repeat arrives every ~30 ms; a stale state from a
                // missed key-up is older than that). Inject a harmless key so Windows sees a
                // "Win + something" chord and does not open its own Start menu on release.
                bool fresh = !_winDown || now - _lastWinDown > 150;
                _lastWinDown = now;
                if (fresh)
                {
                    _winDown = true;
                    _otherKey = false;
                    Native.SendKey(DummyVk, Magic);
                }
            }
            else
            {
                bool tap = _winDown && !_otherKey;
                _winDown = false;
                if (tap) Post(Toggle);
            }
            return Native.CallNextHookEx(_kbHook, code, wParam, lParam);
        }

        if (_winDown && down) _otherKey = true;      // Win+X style chord → let Windows handle it

        if (down && k.vkCode == Native.VK_ESCAPE && (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0)
        {
            Post(Toggle);                               // Ctrl+Esc
            return 1;
        }
        return Native.CallNextHookEx(_kbHook, code, wParam, lParam);
    }

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !StartButtonEnabled) return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
        int msg = (int)wParam;
        if (msg == Native.WM_LBUTTONDOWN)
        {
            var m = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            if (TaskbarInfo.StartButton is { } r && r.Contains(m.pt.X, m.pt.Y))
            {
                _swallowUp = true;
                Post(Toggle);
                return 1;
            }
        }
        else if (msg == Native.WM_LBUTTONUP && _swallowUp)
        {
            _swallowUp = false;
            return 1;
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!FallbackEnabled || idObject != 0 || hwnd == IntPtr.Zero) return;
        if (Native.GetClassName(hwnd) != "Windows.UI.Core.CoreWindow") return;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (!_isStartHost.TryGetValue(pid, out bool isStart))
        {
            isStart = Native.GetProcessImageName(pid).EndsWith("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase);
            if (_isStartHost.Count > 64) _isStartHost.Clear();
            _isStartHost[pid] = isStart;
        }
        if (isStart) Post(Show);
    }

    private static void Post(Action? a)
    {
        if (a != null) Application.Current?.Dispatcher.BeginInvoke(a);
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
    }
}
