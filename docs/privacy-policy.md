# Winly Start — Privacy Policy

_Last updated: 9 September 2026_

**Winly Start does not collect, transmit or share any personal data.**

There is no telemetry, no analytics, no advertising, no accounts and no crash reporting.

## What stays on your PC

Winly Start stores its settings and your layout in your own user folder,
`%LocalAppData%\WinlyStart`:

* `settings.json` — your preferences (theme, rail icons, menu size, account picture path)
* `tiles.json` — your tile and group layout
* `usage.json` — how often you launch each app, used only for "Most used"
* `custom.json` — programs, files and websites you added yourself
* `calendar.json` — your calendar notes and categories
* `web\` — cached favicons and preview images for website tiles
* `error.log`, `debug.log` — written only if something goes wrong, or if you turn on diagnostics

These files never leave your computer. Uninstalling leaves them in place; delete the folder to
remove them.

## Network access

Winly Start makes network requests in exactly one case: when you add a **website** tile, it
requests that website in order to read its icon and the preview image the site publishes, so the
tile can show them. Those requests go directly to the website you chose and to nowhere else. No
identifiers are attached beyond a normal browser-style user agent.

If you never add a website tile, Winly Start makes no network requests at all.

## Permissions

Winly Start runs as a normal user and never requests administrator rights. It reads your installed
applications from the Windows Start Menu folders in order to list them, and it writes a single
optional `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value if you turn on "Start with
Windows". It does not modify Windows itself.

## Contact

Questions or concerns: <https://github.com/Mahi-BD/WinlyStart/issues>
