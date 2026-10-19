using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace PingTray.Tests
{
    /// <summary>
    /// Checks what works without a screen: host names, the settings file, the status and tooltip,
    /// the icons, pings and TCP connections to this computer, and the updater's parts that need
    /// no GitHub. scripts/build.ps1 runs it with the path of the exe it has just built.
    /// </summary>
    static class TestMain
    {
        static int failures;

        static int Main(string[] args)
        {
            HostNameTests();
            SettingsTests();
            StatusTests();
            TooltipTests();
            IconTests();
            PingerTests();
            UpdaterTests(args[0]);
            Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        static void UpdaterTests(string builtExe)
        {
            Equal(new Version(1, 2, 3, 0), Updater.ParseVersion("v1.2.3"), "a tag with v");
            Equal(new Version(1, 2, 0, 0), Updater.ParseVersion("1.2"), "a tag with two parts");
            Equal(null, Updater.ParseVersion("v1.2.0-beta"), "a pre-release tag is no version");
            Equal(null, Updater.ParseVersion("latest"), "a word is no version");
            Equal("1.2.0", Updater.Text(new Version(1, 2, 0, 0)), "versions read like tags");
            Equal(true, Updater.ParseVersion("v99.0") > Updater.CurrentVersion, "a higher tag is newer");
            Equal(false, Updater.ParseVersion("v" + Updater.Text(Updater.CurrentVersion)) > Updater.CurrentVersion,
                "the same tag is not newer");

            const string json = @"{""url"":""https://api.github.com/x"",""html_url"":""https://github.com/t7ko/windows-ping-tray/releases/tag/v1.2.0"",
                ""tag_name"":""v1.2.0"",""draft"":false,""body"":""- Fixes ✓"",""author"":{""login"":""t7ko""},
                ""assets"":[{""name"":""notes.txt"",""browser_download_url"":""https://x/notes.txt"",""size"":3,""digest"":null},
                {""name"":""PingTray.exe"",""browser_download_url"":""https://github.com/d/PingTray.exe"",""size"":81920,""digest"":""sha256:ABCdef01""}]}";
            Release release = Updater.Parse(json);
            Equal(new Version(1, 2, 0, 0), release.Version, "release version");
            Equal("https://github.com/d/PingTray.exe", release.DownloadUrl, "the exe, not the other asset");
            Equal(81920L, release.Size, "asset size");
            Equal("abcdef01", release.Sha256, "digest, lower case");
            Equal("https://github.com/t7ko/windows-ping-tray/releases/tag/v1.2.0", release.PageUrl, "release page");
            Equal(null, Updater.Parse(@"{""tag_name"":""v1.3.0"",""assets"":[]}"), "a release without the exe");
            Equal(null, Updater.Parse(@"{""tag_name"":""nightly"",""assets"":[{""name"":""PingTray.exe"",""browser_download_url"":""u""}]}"),
                "a release whose tag is no version");
            Equal(null, Updater.Parse(@"{""tag_name"":""v1.3.0"",""assets"":[{""name"":""PingTray.exe"",""browser_download_url"":""u""}]}").Sha256,
                "an asset without a digest");

            var good = new Release { Version = Updater.CurrentVersion, Size = new FileInfo(builtExe).Length, Sha256 = Sha256(builtExe) };
            Throws(null, () => Updater.Verify(builtExe, good), "the built exe passes");
            Throws(typeof(InvalidDataException), () => Updater.Verify(builtExe,
                new Release { Version = good.Version, Size = good.Size + 1 }), "a wrong size fails");
            Throws(typeof(InvalidDataException), () => Updater.Verify(builtExe,
                new Release { Version = good.Version, Sha256 = "00" }), "a wrong SHA-256 fails");
            Throws(typeof(InvalidDataException), () => Updater.Verify(builtExe,
                new Release { Version = new Version(9, 9, 9, 0) }), "a wrong version fails");
            string testsExe = typeof(TestMain).Assembly.Location;
            Throws(typeof(InvalidDataException), () => Updater.Verify(testsExe,
                new Release { Version = good.Version }), "another program fails");
            string html = Path.Combine(Path.GetTempPath(), "PingTray-test.html");
            File.WriteAllText(html, "<html>Not found</html>");
            Throws(typeof(InvalidDataException), () => Updater.Verify(html, new Release { Version = good.Version }),
                "a web page fails");
            File.Delete(html);

            string folder = Path.Combine(Path.GetTempPath(), "PingTray-test-update");
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "PingTray.exe");
            File.WriteAllText(exe, "old");
            File.WriteAllText(exe + ".new", "new");
            Updater.Swap(exe, exe + ".new");
            Equal("new", File.ReadAllText(exe), "swap puts the new exe in place");
            Equal("old", File.ReadAllText(exe + ".old"), "swap keeps the old exe");
            Updater.SwapBack(exe, exe + ".new");
            Equal("old", File.ReadAllText(exe), "swap back restores the old exe");
            Updater.Swap(exe, exe + ".new");
            Equal(false, Updater.FinishUpdate(new string[0], exe), "a normal start is no update");
            Equal(false, File.Exists(exe + ".old"), "the next start deletes the old exe");
            Equal(true, Updater.FinishUpdate(new[] { "--updated-from", "999999" }, exe), "a start by an update");
            Directory.Delete(folder, true);

            Equal(SecurityProtocolType.SystemDefault, ServicePointManager.SecurityProtocol,
                "HTTPS uses the TLS versions of Windows (the TargetFramework attribute works)");
        }

        static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Checks that the action throws that type of exception, or nothing when it is null.</summary>
        static void Throws(Type expected, Action action, string what)
        {
            Type thrown = null;
            try
            {
                action();
            }
            catch (Exception e)
            {
                thrown = e.GetType();
            }
            Equal(expected, thrown, what);
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
            Equal("1 of 2 hosts answer\n× google.com: timeout\nya.ru: 12 ms",
                StatusReport.Tooltip(Results(Up("ya.ru", 12), Down("google.com", "timeout")), ping),
                "some down: the failing host first, marked");
            Equal("Offline: no host answers\n× ya.ru: DNS error",
                StatusReport.Tooltip(Results(Down("ya.ru", "DNS error")), ping), "all down");
            Equal("Online (TCP 443)\nya.ru: 12 ms",
                StatusReport.Tooltip(Results(Up("ya.ru", 12)), tcp), "TCP names the port");
            Equal("3 of 5 hosts answer\n× c: timeout\n× e: DNS error\nb: 40 ms\na: 12 ms\nd: <1 ms",
                StatusReport.Tooltip(Results(Up("a", 12), Up("b", 40), Down("c", "timeout"), Up("d", 0),
                    Down("e", "DNS error")), ping),
                "failing hosts in settings order, then the others from slowest to fastest");
            Equal("Online\nx: 10 ms\ny: 10 ms\nz: 10 ms",
                StatusReport.Tooltip(Results(Up("x", 10), Up("y", 10), Up("z", 10)), ping),
                "equal times keep the settings order");

            List<HostResult> crowd = Enumerable.Range(1, 12).Select(i => Up("fast" + i + ".example.com", i)).ToList();
            crowd.Add(Down("down1.example.com", "timeout"));
            crowd.Add(Down("down2.example.com", "unreachable"));
            string crowded = StatusReport.Tooltip(crowd, ping);
            Equal(true, crowded.Contains("× down1.example.com") && crowded.Contains("× down2.example.com"),
                "a long list still shows every failing host");
            Equal(false, crowded.Contains("fast1.example.com:"), "a long list leaves out the fastest hosts");

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
