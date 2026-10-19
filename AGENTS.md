# Notes for AI coding agents: ping-tray

A Windows tray app. It pings a list of hosts on a timer, colors its icon green, yellow or
red by how many of them answer, and shows each host's ping time in the tooltip. The
README describes it as the user sees it. The toolchain and how to test tray apps are in
`../AGENTS.md`.

## Layout

- `src/Program.cs`: entry point, single-instance mutex, assembly version.
- `src/TrayApp.cs`: the tray icon, its menu, the timers and the check loop.
- `src/Pinger.cs`: DNS lookup (IPv4 first), then one ICMP echo or one TCP connection per
  host; never throws.
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
- One ping (or TCP connection) per host per check. A single lost packet shows yellow
  until the next check.
- The TCP method is one setting for all hosts, not a `host:port` per line; `HostNames`
  still drops a pasted port. The time is that of `TcpClient.ConnectAsync`, which comes
  within a few milliseconds of the ping time. A refused connection is a failure.
  Windows retries a refused connection and reports it after about 2 seconds, so with the
  default timeout a closed port usually shows `timeout` rather than `refused`.
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
