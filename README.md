# The Window Hider

A tool that hides any application's windows from screen sharing and recording (Zoom, Teams, Discord, OBS, etc.) while they stay fully visible on your own monitor.

## DISCLAIMER

This project is for **personal and legitimate privacy use only**. It works by injecting a thread into other processes, which antivirus and EDR software will often flag as malicious. Usage of this tool is completely your own responsibility, and I am not responsible for any misuse, flagged binaries, or damage caused by this program.

## What is this?

Windows exposes [`SetWindowDisplayAffinity`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) with the flag `WDA_EXCLUDEFROMCAPTURE` (0x11), which excludes a window from screen capture while leaving it visible locally. Microsoft restricts that call to the process that **owns** the window, so you cannot hide another app's window from the outside. The workaround is already known and was first demonstrated here: [shalzuth/WindowSharingHider](https://github.com/shalzuth/WindowSharingHider) — you create a thread inside the target process and call the API from there.

The Window Hider takes that idea and turns it into a persistent, rule-driven tool with a live watcher, a modern UI, and a hardened injection engine.

## How this program implements this.

1. Enumerate every real top-level window and match it against your rules (by process/exe, window title, or a specific window)
2. For a matching foreign window, `OpenProcess` its owner and detect its bitness
3. Walk `user32.dll`'s PE export table **in the remote process** to resolve the real address of `SetWindowDisplayAffinity`
4. Write a tiny shellcode stub (`SetWindowDisplayAffinity(hWnd, 0x11); ret`) into the process and run it via `CreateRemoteThread`
5. A `SetWindowEvent` hook re-applies this the instant new windows appear, so nothing slips through

Windows your own process owns skip all of that and call the API directly.

## Features

- Rules by **process/exe**, **window title** (is / contains / starts with / ends with / **regex**), or a specific window, plus **exception** rules that always win
- Live auto-reapply via `SetWinEventHook` (no polling)
- Persistent config, system tray, search + real app icons, run-at-startup toggle
- Smarter engine: injects only on state change, caches the resolved API address per process, uses W^X memory, and silently skips processes it can't open
- Portable single exe that can install itself (Start Menu shortcut + Apps &amp; features entry, all per-user, no admin)

## Requirements

- Windows 10 (2004+) or Windows 11
- .NET 10 SDK to build (or the .NET 10 Desktop Runtime to run a published build)
- x64 (so it can inject into both 64-bit and 32-bit targets)

## How to run

Build and run straight from source:

```bash
dotnet run --project src/TheWindowHider
```

Or publish a single self-contained `.exe`:

```bash
dotnet publish src/TheWindowHider -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The app starts hiding as soon as you add a rule. Elevated or protected processes (some games with anti-cheat, DRM apps) cannot be opened and are skipped.

## Installing

The published exe is portable. On its first run it offers, once, to install itself into your programs folder; you can also do this any time from the **Settings** tab. Installing:

- copies the exe to `%LocalAppData%\Programs\TheWindowHider`,
- adds a Start Menu shortcut,
- registers an **Apps &amp; features** entry so it can be uninstalled normally,
- and re-points "start with Windows" at the installed copy.

For scripted deployment there are silent flags:

```bash
TheWindowHider.exe --install --silent      # install with no prompts
TheWindowHider.exe --uninstall --silent    # remove everything it added
```

Settings live in `%AppData%\TheWindowHider\config.json` (independent of where the exe lives), and "start with Windows" is a value under `HKCU\...\CurrentVersion\Run`.

## Credits

Inspired by [shalzuth/WindowSharingHider](https://github.com/shalzuth/WindowSharingHider) for the core technique. Released under the MIT License.
