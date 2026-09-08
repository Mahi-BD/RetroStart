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
    // Masking key used to turn a lone Win tap into a "chord" so the shell does not open its own menu.
    // Must be a key the shell actually counts: the unassigned 0xE8 is ignored on Windows 11 build 26200,
    // whereas VK_CONTROL is always counted and, since only lone taps reach this path, injecting it has no
    // visible effect and cannot corrupt a real Win+X combo.
    private const ushort MaskVk = Native.VK_CONTROL;

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
                // missed key-up is older than that).
                bool fresh = !_winDown || now - _lastWinDown > 150;
                _lastWinDown = now;
                if (fresh) { _winDown = true; _otherKey = false; }
            }
            else
            {
                bool tap = _winDown && !_otherKey;
                _winDown = false;
                if (tap)
                {
                    // A lone Win tap. Swallow this real key-up (return 1) and re-emit the release as a
                    // chord: a harmless dummy key, then a tagged Win-up we let pass. Windows then sees
                    // Win-down · dummy · Win-up — a "Win + something" combo — and does NOT open its own
                    // Start menu. Injecting during the key-up hook and *also* passing the key-up through
                    // does not work: the injected events queue AFTER the real key-up, too late to mask.
                    // Real Win+X chords never reach here (they set _otherKey and pass through untouched),
                    // so swallowing is safe.
                    Native.SendKeys(
                        Native.Key(MaskVk, up: false, Magic),
                        Native.Key(MaskVk, up: true, Magic),
                        Native.Key((ushort)k.vkCode, up: true, Magic));
                    Debug($"winkey tap → toggle (vk={k.vkCode})");
                    Post(Toggle);
                    return (IntPtr)1;
                }
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
            var r = TaskbarInfo.StartButton;
            if (DebugEnabled)
                Debug($"click ({m.pt.X},{m.pt.Y}) startBtn={(r is { } rr ? $"{rr.Left},{rr.Top} {rr.Width}x{rr.Height}" : "null")} hit={(r is { } h && h.Contains(m.pt.X, m.pt.Y))}");
            if (r is { } rect && rect.Contains(m.pt.X, m.pt.Y))
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

    /// <summary>Opt-in trace to %LocalAppData%\RetroStart\debug.log (set env RETROSTART_DEBUG=1). Off by default.</summary>
    internal static readonly bool DebugEnabled =
        Environment.GetEnvironmentVariable("RETROSTART_DEBUG") is { Length: > 0 } v && v != "0";

    internal static void Debug(string msg)
    {
        if (!DebugEnabled) return;
        try
        {
            System.IO.Directory.CreateDirectory(Store.Dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
    }
}
