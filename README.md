# Retro Start

**The Windows 10 Start menu, back on Windows 11.**

Retro Start is a small, open-source (MIT) .NET 8 app that brings the Windows 10 Start menu
experience to Windows 11 — the left rail, the A–Z app list with folders and the alphabet
jump grid, and the tile board — while following your Windows 11 theme (light/dark, accent
colour, transparency) and keeping the Windows 10 *look*: acrylic blur, square corners, the
slide-up animation, tile hover and press effects.

It **replaces** the Windows 11 Start menu in everyday use: the Start button, the Windows
key and Ctrl+Esc all open Retro Start. It does this without touching Windows itself — quit
the app and Windows 11 is exactly as it was.

> Status: **v0.1 — early preview.** It builds cleanly and the design is complete, but it is
> young software. Please open issues with your Windows build number and what you saw.

## Features

| Windows 10 feature | Retro Start |
|---|---|
| Left rail: hamburger, user, Documents, Pictures, Settings, Power | ✅ with the expandable labelled rail |
| Power flyout: Sleep / Shut down / Restart · User flyout: account settings / Lock / Sign out | ✅ |
| "Recently added" (with Expand) and "Most used" | ✅ |
| A–Z list with letter headers, Start-menu folders that expand inline | ✅ |
| Alphabet jump grid (click a letter header) | ✅ |
| Type to search the app list, Enter launches | ✅ |
| Right-click: Pin/Unpin, Run as administrator, Open file location, Uninstall | ✅ |
| Tiles: Small / Medium / Wide / Large, groups with editable names, drag to rearrange | ✅ |
| Packaged (Store) apps with their coloured tile logos | ✅ |
| Acrylic blur, accent colour on Start, transparency on/off — follows Windows 11 settings | ✅ live |
| Live tiles | ❌ the platform API no longer exists on Windows 11 |

## Install

1. Download the latest zip from **Releases** (`RetroStart-win-x64.zip` needs the
   [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0);
   `RetroStart-win-x64-selfcontained.zip` needs nothing).
2. Unzip anywhere and run `RetroStart.exe`. A tray icon appears; press the Windows key.
3. Right-click the tray icon → **Settings…** to enable *Start with Windows*.

Nothing is installed system-wide. To uninstall: tray icon → **Exit Retro Start**, delete the
folder, and optionally delete `%LocalAppData%\RetroStart` (your tile layout and settings).

## How it replaces the Start menu — and why that is safe

Retro Start never injects into `explorer.exe`, never patches files, never writes outside
`HKCU`. It uses three ordinary, revertible user-mode mechanisms:

* **Windows key / Ctrl+Esc** — a low-level keyboard hook. When you tap the Windows key,
  Retro Start lets the key through *and* sends a harmless unassigned key alongside it, so
  Windows sees a "Win + something" chord and does not open its own menu. Every Win+X
  shortcut keeps working natively.
* **Start button** — a low-level mouse hook that recognises a left-click on the taskbar's
  Start button (located through UI Automation, so it works with left- and centre-aligned
  taskbars) and opens Retro Start instead.
* **Fallback** — if the Windows 11 menu still appears (touch gesture, or the Windows key
  pressed while an elevated app has focus, which hooks cannot see), Retro Start notices it
  and takes over; the Windows menu dismisses itself.

Closing Retro Start removes the hooks; Windows 11 is untouched.

## Settings

Tray icon → **Settings…**

* Windows key and Ctrl+Esc open Retro Start · Clicking the Start button opens Retro Start
* Start with Windows (a single `HKCU\…\Run` value — the only registry write Retro Start makes)
* Theme: follow Windows / Light / Dark · Tile columns: 3 or 4 medium tiles · Menu height
* Open under the Start button (for a centred taskbar) instead of the screen corner
* Show "Recently added" / "Most used" · Trim memory when hidden

Data lives in `%LocalAppData%\RetroStart\` — `settings.json`, `tiles.json`, `usage.json`.

## Memory

Retro Start is written to stay small: plain WPF, no UI frameworks, no WinForms, icons loaded
lazily at the exact sizes shown on a single background thread and frozen, a virtualised app
list, and the working set is trimmed when the menu hides. Expect a few tens of MB while the
menu is closed.

## Build from source

```
git clone https://github.com/Mahi-BD/RetroStart
cd RetroStart
dotnet build src/RetroStart -c Release
dotnet publish src/RetroStart -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out
```

Requires the .NET 8 SDK. The project sets `EnableWindowsTargeting`, so it also *compiles*
on Linux/macOS (handy for CI and reviews); it only *runs* on Windows 10/11.

Developer notes, architecture and the project rules are in
[`Repo/Instruction/read.md`](Repo/Instruction/read.md).

## Known limitations (v0.1)

* Opens on the primary monitor's taskbar; multi-monitor placement is on the roadmap.
* Pin to taskbar and jump lists are not available (no public API).
* "Recently added" for packaged apps is based on when Retro Start first saw them.
* While an **elevated** window has focus, the Windows key is handled by the fallback path
  (brief flicker of the Windows 11 menu). Retro Start deliberately does not run elevated.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). The two rules
that matter most: keep the code and the working set small, and never do anything that could
harm the operating system.

## License

[MIT](LICENSE). Windows, Windows 10 and Windows 11 are trademarks of Microsoft Corporation;
Retro Start is an independent project and is not affiliated with Microsoft.
