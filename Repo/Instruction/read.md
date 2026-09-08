# Retro Start — project rules, plan & architecture

**Retro Start** brings the Windows 10 Start menu back to Windows 11. It is an open-source
(MIT) .NET 8 WPF desktop app that *replaces* the Windows 11 Start menu experience
(Start button click, Windows key, Ctrl+Esc) with a faithful Windows 10 style menu —
left rail, A–Z app list, and a live-tile-style tile board — while following the
Windows 11 theme (light/dark, accent colour, transparency) and using Windows 10 style
visuals (acrylic blur, square corners, slide-up animation, tile hover/press effects).

GitHub: https://github.com/Mahi-BD/RetroStart  ·  Local: `/home/mahi/Project/RetroStart`

## Build / run rules (house rules)
1. After any change, **compile it yourself** (`dotnet build src/RetroStart -c Release`) and
   confirm **0 errors** before calling a task done. On Linux the project cross-compiles with
   `EnableWindowsTargeting=true` (use `~/.dotnet/dotnet` if `dotnet` is not on PATH);
   it can only *run* on Windows 10/11.
2. **Minimal code, minimal RAM.** No third-party UI/MVVM frameworks, no WinForms, no
   Windows App SDK / WinUI, no CommunityToolkit. Plain WPF + P/Invoke + `System.Text.Json`
   (source-generated). Every dependency must justify its working-set cost.
3. **Never harm the OS.** No injection into `explorer.exe`, no DLL/registry patching of
   system components, no killing/suspending shell processes, no modifying HKLM.
   Everything Retro Start does is user-mode, HKCU-only, revertible by closing the app.
   The Windows 11 Start menu is *not* disabled — it is intercepted; quit Retro Start and
   Windows is exactly as before.
4. Runs as a **normal user** (`asInvoker`). Never require or auto-request elevation.
5. **Theme follows Windows 11** (registry, live-updated) — never hard-code a colour that
   Windows exposes. Visual *style* follows Windows 10 (square corners, Segoe MDL2 glyphs,
   acrylic tint, 4 px tile gutters, accent-coloured tiles and list headers).
6. Save instructions to `/Repo/Instruction`, scripts to `/Repo/Script`,
   test/scratch files to `/Repo/Temp`. User data lives in `%LocalAppData%\RetroStart\`
   (`settings.json`, `tiles.json`, `usage.json`) — never inside the install folder.
7. Keep `README.md` (user-facing) and this file (developer-facing) in sync when behaviour
   or layout changes.

## Target
- Windows 11 (all builds; acrylic via DWM system backdrop on 22H2+, composition-attribute
  acrylic fallback on older builds and Windows 10).
- .NET 8 Desktop Runtime (framework-dependent build) — a self-contained single-file build is
  produced by CI for users without the runtime.

## How the "replacement" works (no OS harm)
| Trigger | Mechanism | Notes |
|---|---|---|
| **Windows key** tap | `WH_KEYBOARD_LL` hook (`Core/StartHook.cs`). On Win-down we let the key through **and** inject a harmless unassigned virtual key (0xE8) so Windows sees a "combo" and never opens its own Start. On Win-up with no other key pressed → toggle Retro Start. | All Win+X combos keep working natively. Same trick AutoHotkey users have relied on for years. |
| **Ctrl+Esc** | Same hook; swallowed and toggles Retro Start. | |
| **Start button click** | `WH_MOUSE_LL` hook. If a left-click lands inside the Start button rectangle (found through UI Automation, `AutomationId = "StartButton"` inside `Shell_TrayWnd`, cached and refreshed periodically) it is swallowed and Retro Start toggles. | Right-click (Win+X menu) passes through. Works with left- and centre-aligned taskbars. |
| **Anything we missed** (touch gesture, Win key while an elevated window is focused) | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. When the foreground window belongs to `StartMenuExperienceHost.exe`, Retro Start shows itself and takes focus; the Windows 11 menu light-dismisses. | Slight flicker in this rare path only. |

Limitation: low-level hooks cannot see input while an **elevated** window is focused (UIPI).
In that case the fallback row above kicks in. This is by design — we do not run elevated.

## UI layout (Windows 10 1903 style, 100 % DPI values; WPF scales for DPI)
```
┌──────┬────────────────────────┬──────────────────────────────────┐
│ ≡    │ Recently added         │  Productivity                    │
│      │  ▪ App                 │  ┌──────┐┌──────┐┌──────────────┐│
│      │ Most used              │  │      ││      ││              ││
│      │  ▪ App                 │  └──────┘└──────┘└──────────────┘│
│      │ #                      │  Explore                         │
│      │ A                      │  ┌────┐┌────┐┌──────┐            │
│ 👤   │  ▪ App                 │  └────┘└────┘└──────┘            │
│ 📄   │  ▸ Folder              │                                  │
│ 🖼   │ B …                    │                                  │
│ ⚙    │                        │                                  │
│ ⏻    │                        │                                  │
└──────┴────────────────────────┴──────────────────────────────────┘
  48px      248px + scrollbar        6 tile units (3 medium) or 8
```
- Window 648 × 640 (settings: height, tile columns 6/8). Square corners, no title bar,
  `ShowInTaskbar=false`, topmost while open, hides on deactivate / Esc / Win key.
- **Left rail**: hamburger (expands to labelled 248 px overlay), then bottom-aligned:
  user (name + account picture → Change account settings / Lock / Sign out),
  Documents, Pictures, Settings, Power (Sleep / Shut down / Restart).
- **App list**: "Recently added" (≤ 7 days, top 3 + Expand), "Most used" (top 6 from our
  own launch counters), then `#`, `A`–`Z`, `&` headers. Header click → alphabet jump grid
  (available letters enabled). Start-menu sub-folders show as expandable folders.
  Typing while the menu is open filters the list inline; Enter launches the first match.
  Right-click app → Pin to Start / Unpin, More ▸ (Run as administrator, Open file location),
  Uninstall (opens Apps & features).
- **Tiles**: groups with editable names; sizes Small 1×1 (48), Medium 2×2 (100), Wide 4×2
  (204×100), Large 4×4 (204). 4 px gutters. Accent-coloured background, app icon centred,
  name bottom-left on Medium+. Drag to rearrange (first-fit packing), right-click →
  Unpin / Resize / More / Uninstall. Layout persisted to `tiles.json`.
- **Animation**: open = slide up 24 px + fade in 200 ms (ease-out); close = fade 100 ms.
  Tile hover = light border; press = scale 0.97. List item hover = 10 % overlay.
- **Backdrop**: acrylic. Tint `#CC1F1F1F` (dark) / `#CCF2F2F2` (light), or the accent colour
  when Windows "Show accent colour on Start and taskbar" is on. Solid when transparency is off.

## Windows 11 theme integration (`Core/Theme.cs`)
Registry (HKCU), live-refreshed via `SystemEvents.UserPreferenceChanged`:
- `…\Themes\Personalize\SystemUsesLightTheme` → light/dark (Start follows *Windows mode*).
- `…\Themes\Personalize\ColorPrevalence` → accent on Start.
- `…\Themes\Personalize\EnableTransparency` → acrylic vs. solid.
- `…\DWM\AccentColor` (ABGR) → accent brush.
Settings allow forcing Light/Dark.

## App catalogue (`Core/AppCatalog.cs`)
- **Desktop apps**: `*.lnk` / `*.url` under both Start Menu `Programs` folders
  (`%ProgramData%` and `%AppData%`), first-level sub-folder = Start folder, deeper levels
  flattened into it. Created-time drives "Recently added". Watched with `FileSystemWatcher`.
- **Packaged (Store/UWP) apps**: enumerated from `shell:AppsFolder`; items whose
  parent-relative parsing name contains `!` are AUMIDs. Launched with
  `IApplicationActivationManager` (fallback `explorer.exe shell:AppsFolder\<AUMID>`).
- **Icons**: one path for both — `IShellItemImageFactory::GetImage` → `GetDIBits` (top-down
  BGRA) → frozen `BitmapSource`. Loaded lazily on a single background worker, only at the
  sizes needed (32 list, 24/48/96 tiles), cached per app.

## Memory discipline
- Virtualised, recycling `ListBox` for the app list; icons loaded on demand and frozen.
- No `System.Drawing`, no WinForms `NotifyIcon` (tray icon is 60 lines of `Shell_NotifyIcon`).
- Working set trimmed (`SetProcessWorkingSetSize(-1,-1)`) after the menu hides (setting).
- Release build: `ReadyToRun`, `SatelliteResourceLanguages=en`, `DebugType=none`,
  `UseSystemResourceKeys`, no PDBs shipped.

## Source layout
```
src/RetroStart/
  RetroStart.csproj, app.manifest (PerMonitorV2 DPI, asInvoker)
  App.xaml(.cs)          startup, single instance, tray icon, hook wiring
  Core/
    Native.cs            all P/Invoke + COM interop declarations
    ShellIcons.cs        IShellItemImageFactory → BitmapSource
    AppCatalog.cs        AppEntry model + enumeration + usage counters
    Launcher.cs          launch / run-as / open location / uninstall / power / session
    Settings.cs          Settings + TileLayout + Usage models, JSON (source-gen) persistence
    Theme.cs             registry theme/accent → dynamic resource brushes
    StartHook.cs         keyboard/mouse LL hooks + WinEvent fallback (the "replacement")
    TaskbarInfo.cs       taskbar edge/rect, Start button rect (UIA), DPI helpers
    Backdrop.cs          acrylic (DWM system backdrop / composition attribute)
    TrayIcon.cs          Shell_NotifyIcon wrapper
  UI/
    Styles.xaml          Win10 styles: brushes, list item, tile, rail button, scrollbar
    StartMenuWindow.xaml(.cs)  the menu (rail + app list + tiles + search + jump grid)
    TilePanel.cs         Panel that packs tiles into the group grid
    SettingsWindow.xaml(.cs)   options
Repo/Instruction/read.md   this file
.github/workflows/build.yml CI: build on windows-latest, publish zip on tag
```

## Settings (`settings.json`)
`ReplaceWinKey`, `ReplaceStartButton`, `StartWithWindows` (HKCU Run key), `Theme`
(Auto/Light/Dark), `TileColumns` (6/8), `MenuHeight`, `ShowRecentlyAdded`, `ShowMostUsed`,
`OpenAtStartButton` (align under the Win11 Start button instead of the left corner),
`TrimMemoryWhenHidden`.

## Verified on Windows (2026-09-08, build 26200.8037, light theme, centred taskbar)
Tested live on a Windows 11 Pro box (self-contained build). Confirmed working:
- Windows key opens/closes the menu and the **Windows 11 menu is suppressed**; Ctrl+Esc opens it.
- Start-button click opens it (button located via UIA at a centred taskbar), with proper foreground.
- Type-to-search filters the list; clicking a tile or list item launches the app; the menu then
  light-dismisses; clicking outside dismisses it. Theme (light/accent) is picked up live.
- ~65–165 MB working set, no exceptions in `error.log`.

Fixes made during that testing (all in v0.1):
- **Win-key mask.** The unassigned `0xE8` dummy key does *not* register as a chord on build 26200, so
  the mask uses `VK_CONTROL`, and the lone-tap key-up is **swallowed and re-emitted** (dummy + tagged
  Win-up) — injecting during the key-up and letting it pass queues the dummy too late.
- Tile press/launch threw because WPF freezes the template's `ScaleTransform`; each tile now gets its
  own mutable transform on first interaction (`EnsureTransform`).
- A short post-show "deactivation grace" re-asserts foreground when Windows churns focus on open, but
  must bail when the menu is already closing (else launching an app re-opened the menu).
- Opt-in trace to `%LocalAppData%\RetroStart\debug.log` via env `RETROSTART_DEBUG=1`.

Known rough edge: **acrylic renders as a solid tint on build 26200** — `SetWindowCompositionAttribute`
no longer blurs on current Windows 11. Real blur needs the DWM SystemBackdrop path (roadmap). The
surface still tints correctly to the theme.

## Roadmap
- v0.1 (this scaffold): everything above, static tiles, single primary-monitor taskbar.
- v0.2: multi-monitor (open on the monitor whose Start button was clicked), group drag,
  keyboard navigation polish, jump-list on right-click (recent files), localisation.
- v0.3: MSIX/winget package, Start folder ("Windows Tools") virtual folders, tile badges.
- Not planned: true live tiles (the platform API no longer exists on Windows 11).
