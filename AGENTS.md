# Notes for AI coding agents: ping-tray

A Windows tray app. It pings a list of hosts on a timer, colors its icon green, yellow or
red by how many of them answer, and shows each host's ping time in the tooltip. It
updates itself from the GitHub releases of <https://github.com/t7ko/windows-ping-tray>.
The README describes it as the user sees it. The toolchain and how to test tray apps are
in `../AGENTS.md`, outside this repository, as are machine details: the git-ignored
`AGENTS.local.md` has those that belong to this project.

## Layout

- `src/Program.cs`: entry point, single-instance mutex, the version (`AssemblyVersion`).
- `src/TrayApp.cs`: the tray icon, its menu, the timers, the check loop and the update
  flow (daily check, notification, install).
- `src/Pinger.cs`: DNS lookup (IPv4 first), then one ICMP echo or one TCP connection per
  host; never throws.
- `src/StatusReport.cs`: the overall status and the tooltip text.
- `src/Settings.cs`: the settings file (`%APPDATA%\PingTray\settings.ini`) and host-name
  clean-up.
- `src/SettingsForm.cs`: the settings window, built in code (there is no designer).
- `src/Autostart.cs`: the `HKCU\...\Run` value behind Start with Windows.
- `src/Updater.cs`: GitHub's latest-release API, download, checks, and swapping the exe.
- `src/StatusIcons.cs`: draws the icons as .ico data. `scripts/build.ps1` loads this file
  with `Add-Type` to write the exe's icon, so it must compile on its own.
- `tests/Tests.cs`: a console test runner with no framework. `scripts/build.ps1` builds
  the exe into `obj/`, runs the tests with its path (they check it as an update would),
  then copies it to `dist/`.
- `scripts/release.ps1`: tags, pushes and publishes a release (see below).
- `scripts/push.ps1`: pushes `main` (or the refs given) to GitHub with the token.
- `scripts/fake-release-server.js`: a stand-in for GitHub's release API, for trying
  updates.

## Decisions

- .NET Framework 4.8 and the C# 5 compiler that comes with Windows: nothing to install for
  building or running. `src/Program.cs` declares `TargetFramework` 4.8; without it .NET
  runs the exe as if built for 4.0, and HTTPS to GitHub fails for lack of TLS 1.2.
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

## Updates and releases

- A release is a tag `v<major>.<minor>.<build>` with the exe attached under the name
  `PingTray.exe` (`Updater.AssetName`). Installed copies compare the tag of
  `/releases/latest` (which skips drafts and pre-releases) with their own
  `AssemblyVersion`. Tags with a suffix such as `-beta` are ignored. Renaming the asset or
  the repository strands every installed copy.
- The update installs only if the download has GitHub's size and SHA-256 (`digest`, given
  for assets uploaded since mid-2025), is an assembly named `PingTray`, and has the tag's
  version. There is no code signature; the trust is HTTPS and the GitHub account.
- A running exe cannot be overwritten but can be renamed: `PingTray.exe.new` is
  downloaded next to it, the running exe becomes `PingTray.exe.old`, the new one takes its
  name and starts with `--updated-from <pid>`, and the old process exits. The new one
  waits for it (the single-instance mutex), then deletes `.old`; every start deletes
  `.old` and `.new` leftovers. The path stays the same, so Start with Windows keeps
  working. Downloads made by the app carry no
  mark-of-the-web, so SmartScreen does not ask again.
- The first check runs a minute after start, then an hourly timer checks once a day.
  Failed automatic checks are silent; a failed automatic install shows a balloon.
  `updates=auto` is the default.
- To publish: raise `AssemblyVersion` and `AssemblyFileVersion` in `src/Program.cs`,
  commit, run `scripts/release.ps1`. To push without a release: `scripts/push.ps1`.
  Both need the token named in `local.properties` (`github.tokenFile`); `push.ps1`
  hands it to git as an HTTP header through `GIT_CONFIG_*` environment variables.
  Never print a token, or put one on a command line.

## Trying it

- Yellow and red: write `%APPDATA%\PingTray\settings.ini` with hosts such as `192.0.2.1`
  (a documentation address that never answers) and `no-such-host.invalid`, restart the
  app, and read the tooltip as `../AGENTS.md` describes. Delete the file afterwards if the
  user had none.
- `CenterScreen` puts the settings window on the monitor with the mouse pointer.
- `docs/settings.png` (in the README) is the window drawing itself: a throwaway exe
  compiled with `src/` (and `/win32icon` for the title bar icon) opens `SettingsForm`, calls
  `PrintWindow` with `PW_RENDERFULLCONTENT` and crops to `DWMWA_EXTENDED_FRAME_BOUNDS`, so
  nothing behind the window shows. Retake it when the window changes.
- Updates without publishing: compile a copy of `src/` with a higher `AssemblyVersion`
  into a folder, with `latest.json` beside it (`tag_name`, and one asset `PingTray.exe`
  with `browser_download_url` `http://127.0.0.1:<port>/PingTray.exe`, `size` and
  `digest` `sha256:<hex>`). Run `node scripts/fake-release-server.js <folder> <port>`,
  copy `dist/PingTray.exe` to a scratch folder, and start it with
  `PINGTRAY_RELEASES_URL=http://127.0.0.1:<port>/latest`. About a minute later it should
  run as the new version from the same path, with no `.old` or `.new` left. A wrong
  digest should leave the old version running and show a balloon.
- `Updater.FindUpdateAsync` against the real GitHub returns null (HTTP 404) while the
  repository has no release.
