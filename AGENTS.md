# Notes for AI coding agents: ping-tray

A Windows tray app. It pings a list of hosts on a timer, colors its icon green, yellow or
red by how many of them answer, and shows each host's ping time in the tooltip. The
README describes it as the user sees it. The toolchain and how to test tray apps are in
`../AGENTS.md`.

## Layout

- `src/Program.cs`: entry point, single-instance mutex, assembly version.
- `src/TrayApp.cs`: the tray icon, its menu, the timers and the check loop.
- `src/Pinger.cs`: DNS lookup (IPv4 first) and one ICMP echo per host; never throws.
- `src/StatusReport.cs`: the overall status and the tooltip text.
- `src/Settings.cs`: the settings file (`%APPDATA%\PingTray\settings.ini`) and host-name
  clean-up.
- `src/SettingsForm.cs`: the settings window, built in code (there is no designer).
- `src/Autostart.cs`: the `HKCU\...\Run` value behind Start with Windows.
- `src/StatusIcons.cs`: draws the icons as .ico data. `scripts/build.ps1` loads this file
  with `Add-Type` to write the exe's icon, so it must compile on its own.
- `tests/Tests.cs`: a console test runner with no framework; `scripts/build.ps1` runs it
  before building the app.

## Decisions

- .NET Framework 4.8 and the C# 5 compiler that comes with Windows: nothing to install for
  building or running.
- The native tooltip, not a custom pop-up window. `NotifyIcon.Text` refuses more than 63
  characters on .NET Framework; `TrayApp.SetTooltip` sets the private `text` field and
  calls `UpdateIcon` to get the 127 that Windows shows.
- One ping per host per check. A single lost packet shows yellow until the next check.
- Settings are read once at start-up and replaced when the settings window saves them;
  the file is not watched.
- The app is system DPI aware (`src/app.manifest`). Sizes in `SettingsForm` are
  multiples of the font height, and the window is sized from the table layout's
  `PreferredSize` rather than with `Form.AutoSize`.

## Trying it

- Yellow and red: write `%APPDATA%\PingTray\settings.ini` with hosts such as `192.0.2.1`
  (a documentation address that never answers) and `no-such-host.invalid`, restart the
  app, and read the tooltip as `../AGENTS.md` describes. Delete the file afterwards if the
  user had none.
- `CenterScreen` puts the settings window on the monitor with the mouse pointer.
