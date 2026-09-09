# Microsoft Store submission — Winly Start

Winly Start is listed in the Microsoft Store for **free**, using the same information as the GitHub
project. This file records what was submitted and what has to be repeated for every future release.

**Product id:** `f0f7a659-0dc8-4389-919f-e55d8daaed01`
**Dashboard:** <https://partner.microsoft.com/dashboard/win32apps/f0f7a659-0dc8-4389-919f-e55d8daaed01/overview>

## Which submission type

**"EXE or MSI app"** (an *unpackaged* Win32 submission), not MSIX.

Winly Start installs low-level keyboard and mouse hooks and writes an `HKCU\…\Run` value. That is
normal for a Win32 desktop app, but an MSIX/packaged app runs with restrictions that would break the
Start-button and Windows-key interception, which is the whole point of the app. The EXE/MSI route
lets the Store list and update the existing per-user installer as-is.

## ⚠️ The package URL must not redirect

With this route **you host the installer** and give the Store its URL — and Partner Center rejects
anything that redirects:

> The package URL redirects to another URL. Provide a download URL without redirection.

A GitHub *release asset* URL always 302s to a signed, expiring `release-assets.githubusercontent.com`
link, so it can never be used. The installer is mirrored on **GitHub Pages**, which serves it
directly (`200`, `application/octet-stream`):

```
https://mahi-bd.github.io/WinlyStart-downloads/WinlyStart-Setup-1.5.1.exe
```

That mirror lives in its own repo — [`Mahi-BD/WinlyStart-downloads`](https://github.com/Mahi-BD/WinlyStart-downloads)
— so 50 MB installers never enter this repository's history.

**For every new release:** copy the installer into that repo, push, wait for Pages, then update the
package URL in Partner Center. Otherwise the Store keeps handing out the old build.

Silent switches (Inno Setup) as entered:

| Field | Value |
|---|---|
| Silent install | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL` |
| Install scope | Per user (no elevation) |
| App type | EXE |
| Architecture | x64 |
| Exit code — installed | `0` |
| Exit code — cancelled by user | `2` |
| Exit code — reboot required | `8` |

## What was entered

**Availability** — Free, all 240 markets, discoverable in the Store.

**Properties** — Category *Utilities & tools*.
Privacy policy <https://mahi-bd.github.io/WinlyStart/privacy-policy.html> ·
Website <https://mahi-bd.github.io/WinlyStart/> ·
Support <https://github.com/Mahi-BD/WinlyStart/issues>.
No product declarations ticked (no drivers, no NT services, no generative AI).

**Age ratings** — IARC questionnaire, *All Other App Types*, "no" to every content question →
**ESRB Everyone**, **IARC 3+**, **Microsoft 3+**.

**Store listing** — English (United States); copy below; the six 1366×768 screenshots in
`docs/store/`; logos from `docs/store/logos/`.

### Store logos

Partner Center requires **1:1 box art at 1080×1080** (or 2160×2160) and recommends **2:3 poster art
at 720×1080**. Small logos are rejected, and upscaling the 256 px app icon looks soft, so
`docs/store/logos/` contains art *redrawn* at full size:

| File | Use |
|---|---|
| `logo-1x1-1080.png` | 1:1 box art (required) |
| `logo-2x3-720x1080.png` | 2:3 poster art |

### Certification notes

The submission notes state up front that the app installs `WH_KEYBOARD_LL` and `WH_MOUSE_LL` hooks in
its own process to intercept the Start button and the Windows key; that it never injects into
`explorer.exe`, patches no system file, installs no driver or service and logs no keystrokes; that it
runs as the signed-in user and installs per-user; and that exiting it restores Windows immediately.
A tester who finds the hooks without that context is likely to flag the app.

## Listing copy

**Name**

```
Winly Start
```

**Short description**

```
The Windows 10 Start menu, back on Windows 11. Left rail, A-Z app list, live tiles and a tile board - following your Windows 11 theme, and without modifying Windows.
```

**Description**

```
Winly Start brings the Windows 10 Start menu experience to Windows 11.

The Start button, the Windows key and Ctrl+Esc all open Winly Start instead of the Windows 11 menu - and it does that without modifying Windows. Quit Winly Start and the Windows 11 Start menu is back instantly.

FAMILIAR LAYOUT
• Left rail with your account, folders and Power
• "Recently added" and "Most used"
• A-Z app list with expandable Start menu folders and an alphabet jump grid
• A tile board with named groups

LIVE TILES
Windows 11 removed live tiles, so Winly Start provides its own: a Calendar tile showing your next note, a Clock tile, a Photos slideshow from your Pictures folder, and website tiles that show the site's own preview image. Every tile has a Turn Live Tile on/off switch.

TILES YOU CAN ARRANGE
Drag tiles and the others glide out of the way. Resize between Small, Medium, Wide and Large. Create groups, rename them, drag them to reorder, and drag the menu's corner to resize the whole thing.

ADD ANYTHING TO START
Right-click empty space to add any program, file, shortcut or website. Websites use the site's favicon as their icon. Anything you add can be renamed or re-pointed later.

CALENDAR
A three-month calendar with a note per day, colour-coded by category. Add, rename, recolour and delete your own categories. Import and export notes as JSON or iCalendar (.ics).

FITS YOUR WINDOWS
Follows your Windows 11 light/dark mode, accent colour and transparency setting, and opens where your Start button is - centred taskbar or left-aligned.

LIGHT ON RESOURCES
Plain WPF with no heavy frameworks: icons load lazily at the exact sizes shown, the app list is virtualised, live-tile timers run only while the menu is open, and memory is trimmed when the menu hides.

SAFE BY DESIGN
Winly Start never injects into Explorer, never patches system files and never writes outside your own user settings. It runs as a normal user and never asks for administrator rights. Open source under the MIT licence.

Not affiliated with Microsoft. Windows, Windows 10 and Windows 11 are trademarks of Microsoft Corporation.
```

**Product features**

```
Windows 10 layout: left rail, A-Z app list and a tile board with named groups
Live tiles: Calendar, Clock, a Photos slideshow and website previews
Drag, resize and group your tiles - Small, Medium, Wide and Large
Pin any program, file, shortcut or website to Start
Follows your Windows 11 theme, accent colour and taskbar alignment
Never modifies Windows - quit it and Windows 11 is exactly as it was
```

**Search terms**

```
start menu, windows 10 start, live tiles, start menu replacement, classic start, tiles, taskbar
```

**Copyright / trademark**

```
© Winly Start contributors. MIT licence. Not affiliated with Microsoft.
```

**Applicable license terms** — the full MIT licence text, plus the source URL and the
"not affiliated with Microsoft" disclaimer.

## Submitted

Submitted **2026-09-09**. Status **In review** (Microsoft's SLA is 3 business days).
All five sections green: Availability, Properties, Age ratings, Packages, Store listing.
Store ID and the Store deep link only appear once the app goes live.

### Package validation result (package 28995904, x64)

| Check | Result |
|---|---|
| Malware check | **Passed** — "The package is found to be clean." |
| Code sign check | **Passed** — "Your app has a valid code sign." |
| Silent install check | Unknown — "We could not identify if your app is installing silently." |
| Entry in add or remove programs | Unknown — could not identify the app and publisher name |
| Bundleware check | Unknown — same reason |

The three "unknown" results are the expected shape of a **per-user** Inno Setup install: the
validation sandbox looks for a machine-wide Add/Remove Programs entry, and Winly Start writes its
uninstall entry under `HKCU` instead. They are not failures, but they are the most likely thing a
certification tester asks about — the certification notes already explain the per-user install.

⚠️ The run takes far longer than the "approximately 30 mins" the page claims, and **the page does not
refresh itself**: it showed both checks spinning for over an hour, and a hard reload after submitting
revealed both had actually passed. Reload before concluding a run is stuck.
