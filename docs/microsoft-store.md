# Microsoft Store submission — Winly Start

Everything needed to list Winly Start in the Microsoft Store, free, using the same information as
the GitHub project. The listing copy below is ready to paste into Partner Center.

> **Submitting has to be done from the owner's Partner Center account** (sign-in + MFA, and the
> publisher agreement is accepted under a real identity). This file prepares the submission; it does
> not perform it.

## Which submission type

Use **"EXE or MSI app"** (an *unpackaged* Win32 submission), not MSIX.

Reason: Winly Start installs low-level keyboard and mouse hooks and writes an `HKCU\…\Run` value.
That is normal for a Win32 desktop app, but an MSIX/packaged app runs with restrictions that would
break the Start-button and Windows-key interception, which is the whole point of the app. The
EXE/MSI route lets the Store list and update the existing per-user installer as-is.

With this route **you host the installer** and give the Store its URL. Use the release asset:

```
https://github.com/Mahi-BD/WinlyStart/releases/download/v1.5.0/WinlyStart-Setup-1.5.0.exe
```

Silent install/uninstall switches Partner Center asks for (Inno Setup):

| Field | Value |
|---|---|
| Silent install | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL` |
| Silent uninstall | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` |
| Install scope | Per user (no elevation) |
| Installer type | Inno Setup |

## Steps in Partner Center

1. <https://partner.microsoft.com/dashboard/apps-and-games/overview> → **New product** → **EXE or MSI app**.
2. **Reserve the name**: `Winly Start`. (If taken, `Winly Start — Windows 10 Start Menu`.)
3. **Pricing and availability** → Free, all markets, all Windows 10/11 desktop devices.
4. **Properties** → Category **Utilities & tools**, subcategory *File managers* or *Personalization*.
   Support contact: the GitHub issues URL. Privacy policy URL: see below.
5. **Store listing** → paste the copy below, upload the screenshots in `docs/store/`.
6. **Packages** → installer URL + the silent switches above.
7. **Submit** for certification.

## Listing copy

**Name**

```
Winly Start
```

**Short description** (max 200)

```
The Windows 10 Start menu, back on Windows 11. Left rail, A–Z app list, live tiles and a tile board — following your Windows 11 theme, and without modifying Windows.
```

**Description**

```
Winly Start brings the Windows 10 Start menu experience to Windows 11.

The Start button, the Windows key and Ctrl+Esc all open Winly Start instead of the Windows 11 menu — and it does that without modifying Windows. Quit Winly Start and the Windows 11 Start menu is back instantly.

FAMILIAR LAYOUT
• Left rail with your account, folders and Power
• "Recently added" and "Most used"
• A–Z app list with expandable Start menu folders and an alphabet jump grid
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
Follows your Windows 11 light/dark mode, accent colour and transparency setting, and opens where your Start button is — centred taskbar or left-aligned.

LIGHT ON RESOURCES
Plain WPF with no heavy frameworks: icons load lazily at the exact sizes shown, the app list is virtualised, live-tile timers run only while the menu is open, and memory is trimmed when the menu hides.

SAFE BY DESIGN
Winly Start never injects into Explorer, never patches system files and never writes outside your own user settings. It runs as a normal user and never asks for administrator rights. Open source under the MIT licence.

Not affiliated with Microsoft. Windows, Windows 10 and Windows 11 are trademarks of Microsoft Corporation.
```

**Search terms** (up to 7)

```
start menu, windows 10 start, live tiles, start menu replacement, classic start, tiles, taskbar
```

**Copyright / trademark**

```
© Winly Start contributors. MIT licence. Not affiliated with Microsoft.
```

## Still needed before submitting

1. **A privacy policy URL** — Partner Center requires one even for an app that collects nothing.
   Publish `docs/privacy-policy.md` (in this repo) as a page, e.g. via GitHub Pages:
   `https://mahi-bd.github.io/WinlyStart/privacy-policy.html`.
2. **Age rating questionnaire** — answer "no" to everything; it will come out as *Everyone*.
3. **A Partner Center developer account** (one-off registration fee) if the account is not already
   registered as a publisher.

## Certification notes

Be upfront in the submission notes that the app installs low-level keyboard/mouse hooks in order to
intercept the Start button and the Windows key, that it is user-mode only, and that closing it
restores Windows. Testers who do not know this may otherwise flag the hooking behaviour.
