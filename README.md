# Ping Tray

A small Windows tray app that shows whether the internet is reachable. It pings a few
hosts at a set interval (or connects to a TCP port of each, where ping is blocked) and
colors its icon in the notification area:

- green: every host answers;
- yellow: some hosts do not answer;
- red: no host answers;
- gray: not checked yet, or there are no hosts to ping.

Hover over the icon to see the ping time of each host:

```text
2 of 3 hosts answer
ya.ru: 175 ms
google.com: 123 ms
192.0.2.1: timeout
```

A host that fails says why: `timeout`, `DNS error`, `DNS timeout`, `unreachable`,
`refused` (TCP only), `TTL expired` or `failed`.

## Installing

Build `dist\PingTray.exe` (see below), copy it to a folder of its own, such as
`%LOCALAPPDATA%\Programs\PingTray`, and run it. It needs nothing else: .NET Framework 4.8
is part of Windows 10 and 11.

Windows 11 puts new tray icons behind the ^ arrow. To keep the icon on the taskbar, drag
it out of there, or turn it on in Settings > Personalization > Taskbar > Other system
tray icons.

## Using it

- Hover over the icon: the ping time of each host.
- Double-click it: Settings.
- Right-click it: Check now, Settings, Exit.

Settings has the hosts to check, one per line (host names or IP addresses; a pasted URL
such as `https://ya.ru/` is cut down to its host), how often to check them (1 to 3600
seconds), how long to wait for each answer (100 to 10000 ms), how to check them (ping,
or a TCP connection to a port), and Start with Windows.

Each check tries every host once, all at the same time. The DNS lookup and the ping or
connection each get up to the timeout. Ping Tray prefers a host's IPv4 address, because
many connections have no working IPv6. It also checks 2 seconds after the network
changes, such as when Wi-Fi drops or a VPN connects, instead of waiting for the next
interval.

### Networks that block ping

Some networks (company firewalls, VPNs, guest Wi-Fi) drop ping, so the icon stays red
there even though web sites open. In Settings, choose Connect to TCP port instead. Ping
Tray then opens a TCP connection to that port of each host and closes it at once; the
time shown is that of the connection handshake, which is about the same as a ping. Port
443 (HTTPS) works for web sites and for `1.1.1.1` and `8.8.8.8`. A refused connection
counts as a failure, because a firewall may be the one refusing it. The tooltip's first
line then ends with the port, such as `Online (TCP 443)`.

A tooltip holds at most 127 characters (a Windows limit). Hosts that do not fit are
counted in a last line, such as `+3 more`.

The settings are stored in `%APPDATA%\PingTray\settings.ini`. Start with Windows adds the
value `PingTray` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the
exe's current path; after moving the exe, turn it on again.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

This runs the tests in `tests\Tests.cs`, then writes `dist\PingTray.exe`. It uses the C#
compiler that comes with .NET Framework 4.8, so nothing has to be installed. Exit Ping
Tray before building, or the exe cannot be replaced.
