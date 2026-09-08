# Contributing to Retro Start

Thanks for helping bring the Windows 10 Start menu back!

## Ground rules

1. **Never harm the OS.** No process injection, no patching of system files, no writes
   outside `HKCU`, no elevation. Everything must be revertible by closing the app.
2. **Small code, small RAM.** Plain WPF + P/Invoke. No new NuGet packages or UI frameworks
   without a discussion in an issue first. If a feature costs 10 MB of working set, it needs
   a very good reason.
3. **Theme follows Windows 11, style follows Windows 10.** Read colours from the OS; keep
   square corners, Segoe MDL2 glyphs, 4 px tile gutters.
4. `dotnet build src/RetroStart -c Release` must produce **0 errors and 0 warnings**.

## Workflow

* Open an issue describing the change before large PRs.
* Branch from `main`, keep PRs focused, describe how you tested (Windows build number,
  taskbar alignment, DPI, light/dark).
* Architecture and conventions: [`Repo/Instruction/read.md`](Repo/Instruction/read.md).

## Reporting bugs

Include: Windows version (`winver`), .NET runtime version, whether the taskbar is left- or
centre-aligned, display scaling, and the contents of `%LocalAppData%\RetroStart\error.log`
if it exists.
