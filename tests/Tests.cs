using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace PingTray.Tests
{
    /// <summary>
    /// Checks what works without a screen: host names, the settings file, the status and tooltip,
    /// the icons, and pings and TCP connections to this computer. scripts/build.ps1 runs it
    /// before building the app.
    /// </summary>
    static class TestMain
    {
        static int failures;

        static int Main()
        {
            HostNameTests();
            SettingsTests();
            StatusTests();
            TooltipTests();
            IconTests();
            PingerTests();
            Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        static void HostNameTests()
        {
            Equal("ya.ru", HostNames.Normalize("  Ya.RU "), "trims and lowercases");
            Equal("ya.ru", HostNames.Normalize("https://ya.ru/search?q=1"), "drops the scheme and path");
            Equal("google.com", HostNames.Normalize("google.com:443"), "drops the port");
            Equal("google.com", HostNames.Normalize("google.com."), "drops the root dot");
            Equal("::1", HostNames.Normalize("[::1]:80"), "unwraps a bracketed IPv6 address");
            Equal("2001:db8::1", HostNames.Normalize("2001:db8::1"), "keeps a bare IPv6 address");
            Equal("", HostNames.Normalize("   "), "blank line");
            Equal(true, HostNames.IsValid("ya.ru"), "a name is valid");
            Equal(true, HostNames.IsValid("1.1.1.1"), "an IPv4 address is valid");
            Equal(true, HostNames.IsValid("::1"), "an IPv6 address is valid");
            Equal(true, HostNames.IsValid("пример.рф"), "an internationalized name is valid");
            Equal("xn--e1afmkfd.xn--p1ai", HostNames.ToAscii("пример.рф"), "internationalized name for DNS");
            Equal(false, HostNames.IsValid("not a host"), "spaces are not valid");
            Equal(false, HostNames.IsValid(""), "empty is not valid");
        }

        static void SettingsTests()
        {
            var settings = new Settings
            {
                Hosts = new List<string> { "ya.ru", "8.8.8.8" },
                IntervalSeconds = 30,
                TimeoutMs = 1500,
                Method = CheckMethod.Tcp,
                TcpPort = 8443,
            };
            Settings back = Settings.Parse(settings.Format());
            Equal("ya.ru 8.8.8.8", string.Join(" ", back.Hosts), "hosts survive saving");
            Equal(30, back.IntervalSeconds, "interval survives saving");
            Equal(1500, back.TimeoutMs, "timeout survives saving");
            Equal(CheckMethod.Tcp, back.Method, "method survives saving");
            Equal(8443, back.TcpPort, "port survives saving");
            Equal(CheckMethod.Ping, Settings.Parse(new Settings().Format()).Method, "ping survives saving");
            Equal(CheckMethod.Ping, new Settings().Method, "ping is the default");
            Equal(443, new Settings().TcpPort, "443 is the default port");
            Settings odd = Settings.Parse("method=TCP\ntcp_port=70000\n");
            Equal(CheckMethod.Tcp, odd.Method, "method ignores case");
            Equal(Settings.MaxPort, odd.TcpPort, "port is clamped");
            Equal(CheckMethod.Ping, Settings.Parse("method=carrier pigeon\n").Method, "an unknown method means ping");

            Settings messy = Settings.Parse("interval_seconds=0\r\ntimeout_ms=99999\r\nhost=YA.ru\r\nhost=ya.ru\r\n" +
                "nonsense\r\n# host=commented.out\r\nhost = https://google.com/ \r\n");
            Equal(Settings.MinIntervalSeconds, messy.IntervalSeconds, "interval is clamped");
            Equal(Settings.MaxTimeoutMs, messy.TimeoutMs, "timeout is clamped");
            Equal("ya.ru google.com", string.Join(" ", messy.Hosts), "hosts are normalized, deduplicated, comments skipped");

            Settings bad = Settings.Parse("interval_seconds=abc\n");
            Equal(0, bad.Hosts.Count, "a file without hosts means no hosts");
            Equal(new Settings().IntervalSeconds, bad.IntervalSeconds, "a bad number keeps the default");
            Equal("ya.ru google.com 1.1.1.1", string.Join(" ", new Settings().Hosts), "default hosts");
        }

        static void StatusTests()
        {
            Equal(Status.Unknown, StatusReport.Overall(Results()), "no hosts");
            Equal(Status.Good, StatusReport.Overall(Results(Up("a", 5), Up("b", 7))), "all up");
            Equal(Status.Partial, StatusReport.Overall(Results(Up("a", 5), Down("b", "timeout"))), "some down");
            Equal(Status.Down, StatusReport.Overall(Results(Down("a", "timeout"), Down("b", "DNS error"))), "all down");
        }

        static void TooltipTests()
        {
            var ping = new Settings();
            var tcp = new Settings { Method = CheckMethod.Tcp, TcpPort = 443 };
            Equal("Online\nya.ru: 12 ms\n1.1.1.1: <1 ms",
                StatusReport.Tooltip(Results(Up("ya.ru", 12), Up("1.1.1.1", 0)), ping), "all up");
            Equal("1 of 2 hosts answer\nya.ru: 12 ms\ngoogle.com: timeout",
                StatusReport.Tooltip(Results(Up("ya.ru", 12), Down("google.com", "timeout")), ping), "some down");
            Equal("Offline: no host answers\nya.ru: DNS error",
                StatusReport.Tooltip(Results(Down("ya.ru", "DNS error")), ping), "all down");
            Equal("Online (TCP 443)\nya.ru: 12 ms",
                StatusReport.Tooltip(Results(Up("ya.ru", 12)), tcp), "TCP names the port");

            List<HostResult> many = Enumerable.Range(1, 20).Select(i => Up("host" + i + ".example.com", 100)).ToList();
            string tip = StatusReport.Tooltip(many, tcp);
            string[] lines = tip.Split('\n');
            Equal(true, tip.Length <= StatusReport.MaxTooltipLength, "a long list fits: " + tip.Length);
            Equal("+" + (20 - (lines.Length - 2)) + " more", lines.Last(), "says how many hosts are left out");

            string longName = new string('x', 200) + ".com";
            Equal(true, StatusReport.Tooltip(Results(Up(longName, 1)), tcp).Length <= StatusReport.MaxTooltipLength,
                "a very long name fits");
        }

        static void IconTests()
        {
            foreach (int size in new[] { 16, 20, 24, 32 })
                using (Icon icon = StatusIcons.CreateIcon(Status.Good, size))
                    Equal(size, icon.Width, "icon size " + size);

            using (Icon icon = StatusIcons.CreateIcon(Status.Partial, 32))
            using (Bitmap bitmap = icon.ToBitmap())
            {
                Equal(StatusIcons.FillColor(Status.Partial).ToArgb(), bitmap.GetPixel(16, 16).ToArgb(), "the dot has the status color");
                Equal(0, (int)bitmap.GetPixel(0, 0).A, "the corners are transparent");
            }

            string path = Path.Combine(Path.GetTempPath(), "PingTray-test.ico");
            StatusIcons.WriteIco(path, Status.Good, new[] { 16, 32, 256 });
            using (var icon = new Icon(path, 32, 32))
                Equal(32, icon.Width, "the .ico file loads");
            File.Delete(path);
        }

        static void PingerTests()
        {
            var ping = new Settings { TimeoutMs = 1000 };
            HostResult loopback = Pinger.CheckAsync("127.0.0.1", ping).Result;
            Equal(true, loopback.Ok, "127.0.0.1 answers (" + loopback.Error + ")");
            HostResult localhost = Pinger.CheckAsync("localhost", ping).Result;
            Equal(true, localhost.Ok, "localhost answers (" + localhost.Error + ")");
            HostResult missing = Pinger.CheckAsync("no-such-host.invalid", new Settings()).Result;
            Equal(true, !missing.Ok && missing.Error.StartsWith("DNS"), "an unknown name is a DNS failure (" + missing.Error + ")");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var tcp = new Settings { Method = CheckMethod.Tcp, TcpPort = port, TimeoutMs = 1000 };
            HostResult open = Pinger.CheckAsync("localhost", tcp).Result;
            Equal(true, open.Ok, "an open TCP port answers (" + open.Error + ")");
            listener.Stop();
            // Windows retries a refused connection and reports it after about 2 seconds.
            tcp.TimeoutMs = 5000;
            HostResult closed = Pinger.CheckAsync("127.0.0.1", tcp).Result;
            Equal("refused", closed.Error, "a closed TCP port is refused");
            // 192.0.2.1 is a documentation address: nothing there answers.
            var silent = new Settings { Method = CheckMethod.Tcp, TcpPort = 443, TimeoutMs = 300 };
            HostResult nobody = Pinger.CheckAsync("192.0.2.1", silent).Result;
            Equal(true, !nobody.Ok && (nobody.Error == "timeout" || nobody.Error == "unreachable"),
                "a silent TCP host times out (" + nobody.Error + ")");
        }

        static HostResult Up(string host, long ms)
        {
            return new HostResult { Host = host, Ok = true, RoundtripMs = ms };
        }

        static HostResult Down(string host, string error)
        {
            return new HostResult { Host = host, Error = error };
        }

        static List<HostResult> Results(params HostResult[] results)
        {
            return results.ToList();
        }

        static void Equal<T>(T expected, T actual, string what)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
                return;
            failures++;
            Console.WriteLine("FAIL " + what + ": expected <" + expected + ">, got <" + actual + ">");
        }
    }
}
