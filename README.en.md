# ZoneQuanta

<img src="docs/icon.png" alt="ZoneQuanta icon" width="96" align="right">

[中文](README.md) · **English**

**A Windows desktop world clock with a taskbar network / CPU / RAM monitor and per-app traffic statistics.** Shows local time and Los Angeles time (automatic DST), any of 36 popular cities, live upload / download speed in the taskbar, and how much traffic each application used. Lightweight WPF / .NET 8, one self-contained exe, signed auto-update.

> Keywords: world clock, time zone clock, DST, daylight saving time, Los Angeles time, desktop widget, taskbar monitor, network speed meter, upload download speed, bandwidth monitor, CPU usage, memory usage, per-app network traffic, data usage by app, Windows 10, Windows 11, WPF, .NET 8

## Features

| Feature | Details |
|---|---|
| Local + Los Angeles time | DST / standard time switches automatically using the Windows time zone database (per-year rules). Click a card to flip it: current state, next switch moment, this year's DST period (2026: Mar 8 02:00 – Nov 1 02:00), and today's sunrise / sunset / day length |
| 24-hour day ribbon | A ribbon across each card covering 0–24 h (one tick per hour, labelled 0 / 6 / 12 / 18 / 24): the daylight segment is computed per city from latitude and date (polar day / night included), the part of the day already gone is bright and the rest dimmed, a sun / moon marker sits on the current time, and sunrise / sunset times are printed on the ribbon; hover the ribbon to read any moment; cards tilt in 3D with a moving highlight; the desktop widget uses a compact layout (name + time + slim ribbon + key marks, about half the previous area) and flips on click for DST and sunrise / sunset details |
| Taskbar hover details | Rest the mouse on the taskbar monitor to see upload / download / total speed with a live chart, today's traffic, adapter and link rate, CPU usage with cores / processes / threads, memory used / available / committed, uptime and today's top apps |
| Popular countries and cities | 36 built-in cities (USA, Canada, China, Japan, Europe, Australia, ...), plus search over every system time zone. Up to 4 zones, renamable and reorderable |
| Taskbar monitor | Placed at the far left of the taskbar by default; choose "left of Start" or "left of the notification area" and fine-tune the offset. Height adapts to the taskbar with no padding. Upload speed, download speed, today’s data usage, total speed, memory % and CPU % can each be switched on or off |
| Speed units | Bytes `B` or bits `b`; unit auto (B → KB → MB → GB) or fixed K / M / G |
| Per-app traffic | Upload / download per application over: today, 24 hours, this week, this month, this year, all time. Live speed chart and animated bars |
| Desktop widget | Click-through, always on top, lock position, may extend past screen edges, auto-hide when a full-screen app runs; stays on screen when the desktop is shown (Win+D), everything is minimized (Win+M) or any window is minimized |
| Start with Windows | Per-user startup entry, no administrator rights needed |
| Look and feel | Four eye-friendly low-saturation themes; rolling digits, card flip, hover and page transitions (2D animation), all optional |
| Tray and hotkey | Left-click the tray icon to show / hide the panel; right-click for common switches (including the taskbar monitor); global hotkey `Ctrl + Alt + Z` by default; right-click today's data to open Traffic; right-click speed / CPU / RAM to open Monitor settings |
| Auto-update | Parallel multi-mirror download, ECDSA signature + SHA-256 verification |
| Display speed | A dedicated high-resolution timer thread wakes about 12 ms before each second, so the new second is presented in the frame that contains the boundary; sampling runs on a background thread and the taskbar numbers refresh every 0.5 s by default (0.25 / 0.5 / 1 / 2 s selectable); the hover card opens in about 10 ms; the monitor follows an auto-hidden taskbar within a few milliseconds of it appearing |
| Low footprint | Redraws only what changes; sampling drops to once a second while the band is tucked away and the panel is closed; about 75 MB RAM and normally under 1% of one core when idle |

## v1.3.1

Fixed the clock and taskbar monitor disappearing on "minimize". Showing the desktop (Win+D, Win+M, the corner button) makes Windows report the user state as "busy", exactly as it does for a full-screen app, so the program mistook it for one and hid the widget and the band. A foreground desktop, taskbar or one of our own windows is no longer treated as full screen, and a window the shell iconifies or hides is brought back at once. Measured on the real desktop: the widget and band stay visible throughout Win+D and Win+M, and nothing waits for the next second when leaving show-desktop.

## v1.3.0

This release is about display speed. The clock now comes from a dedicated high-resolution thread that wakes about 12 ms before each second and is drawn first on the UI thread, instead of queueing behind adapter sampling and taskbar relayout. Measured: the new second's text is ready about 11 ms before the flip (the previous build was 13 ms late on average), and under full CPU load the previous build stalled for up to 10 s while this one does not (the process is no longer demoted to below-normal priority and opts out of Windows 11 efficiency-mode throttling). Network / CPU / memory are sampled on a background thread and refresh every 0.5 s by default (0.25 / 1 / 2 s in the Monitor page); sampling drops to once a second while the band is tucked away and the panel is closed.

Taskbar monitor: with an auto-hidden taskbar the layout is read while it is tucked away, so the band appears within a few milliseconds of the taskbar returning (about 0.8 s the first time before). A layout change no longer hides the band and recomputes it; value changes swap text instead of re-running the font search; the hover card is mouse-event driven with its window pre-created, opening in about 10 ms. Fullscreen and "back to desktop" detection now runs the moment the foreground window changes.

Also fixed: refreshing the adapter list every 30 s produced one zero-speed second and dropped that second's traffic from the totals; when the per-app helper's scheduled task disappears the switch no longer stays on silently (it now says so and turns off); the log is rotated instead of wiped at its size limit. The tray menu gains a "Taskbar monitor" switch.


## Install

Download `ZoneQuanta.exe` (about 165 MB) from [Releases](https://github.com/Aevorine/ZoneQuanta/releases/latest) and double-click it. **The .NET runtime and every dependency are built in — nothing else to install.**

On first run you choose an install location. **Pick any folder; the app always installs into a `ZoneQuanta` folder inside it** (choose `D:\Apps` and it installs to `D:\Apps\ZoneQuanta`). A desktop shortcut and start-with-Windows are optional. The exe is not code-signed, so SmartScreen may warn about an unknown publisher: choose "Run anyway".

## Per-app traffic and administrator rights

Windows only lets administrators read per-process network events, so this feature is **optional**. Turning it on in the Traffic page triggers one system authorization and creates a scheduled task `ZoneQuantaTraffic` that runs a small helper with administrator rights (ETW `Microsoft-Windows-Kernel-Network`; it records only process names and byte counts, never content). Turning the switch off deletes the task. Without it you still get total traffic and live speed.

With a local proxy or TUN adapter, traffic is attributed to the proxy process.

## Usage

- Left-click the tray icon to open / close the panel; the taskbar entry disappears when it is closed
- Eight pages: Console, Display, Time zones, Monitor, Traffic, Window, System, Update
- Settings, logs and statistics live in `%APPDATA%\ZoneQuanta`

## Architecture

```
src/ZoneQuanta
├── Core        pure logic, no UI dependency
│   ├── Settings   settings model and persistence
│   ├── Time       time zones, DST, sunrise / sunset (astronomical), city catalog, high-resolution pre-boundary ticker
│   ├── Monitor    background sampler (network / CPU / memory), unit conversion, traffic store
│   ├── Traffic    elevated statistics helper (ETW) and scheduled-task launcher
│   └── Update     manifest verification, parallel download, update coordinator
├── Platform    Win32 wrappers: window styles, full-screen detection, foreground watcher, taskbar tracking and layout, hotkey, autostart
├── Shell       tray icon
├── UI          Theme, Controls, Widget, Band (taskbar monitor), Setup (installer), Panel
└── AppController  composition root
```

## Build

Requires the .NET 8 SDK: `dotnet build src/ZoneQuanta -c Release`. The release flow is `scripts/release.ps1` (build, sign, create the Release, keep only the latest two). The signing key is not in this repository.

## License

MIT
