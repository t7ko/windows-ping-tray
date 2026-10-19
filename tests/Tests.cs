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
            LinksTests();
            JournalTests();
            HoverTests();
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

        static void JournalTests()
        {
            Equal("45 s", Journal.Duration(TimeSpan.FromSeconds(45.7)), "seconds");
            Equal("2 min 12 s", Journal.Duration(TimeSpan.FromSeconds(132)), "minutes");
            Equal("3 h 5 min", Journal.Duration(new TimeSpan(3, 5, 59)), "hours");
            Equal("2 d 4 h", Journal.Duration(new TimeSpan(2, 4, 30, 0)), "days");
            Equal("0 s", Journal.Duration(TimeSpan.FromSeconds(-3)), "a clock set back");

            string path = Path.Combine(Path.GetTempPath(), "PingTray-test", "journal.txt");
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path), true);
            var journal = new Journal(path);
            var heard = new List<string>();
            journal.Added += heard.Add;
            var start = new DateTime(2026, 10, 19, 20, 0, 0);
            journal.Started("1.2.0", start);
            journal.Record(Results(Up("a", 10), Up("b", 20)), start.AddSeconds(1));
            journal.Record(Results(Up("a", 10), Down("b", "timeout")), start.AddSeconds(61));
            journal.Record(Results(Up("a", 12), Down("b", "unreachable")), start.AddSeconds(121));
            journal.Record(Results(Down("a", "timeout"), Down("b", "timeout")), start.AddSeconds(181));
            journal.Record(Results(Up("a", 10), Up("b", 20)), start.AddSeconds(313));
            journal.Record(Results(Up("a", 11), Up("b", 21)), start.AddSeconds(373));
            journal.Record(Results(Up("a", 10), Down("b", "timeout")), start.AddMinutes(50));
            journal.Sleeping(start.AddHours(1));
            journal.Sleeping(start.AddHours(1).AddSeconds(5));
            journal.Record(Results(Down("a", "timeout"), Down("b", "timeout")), start.AddHours(2));
            DateTime wake = start.AddHours(9);
            journal.WokeUp(wake);
            journal.Record(Results(Down("a", "timeout"), Up("b", 5)), wake.AddSeconds(5));
            journal.Record(Results(Down("a", "timeout"), Up("b", 5)), wake.AddSeconds(20));
            journal.Started("1.2.0", wake.AddMinutes(1));
            journal.Record(Results(Up("a", 1), Up("b", 1)), wake.AddMinutes(2));
            journal.Record(Results(), wake.AddMinutes(3));
            string[] expected =
            {
                "2026-10-19 20:00:00  Ping Tray 1.2.0 started",
                "2026-10-19 20:01:01  Not answering: b (timeout)",
                "2026-10-19 20:03:01  Offline: no host answers",
                "2026-10-19 20:05:13  Online again after 4 min 12 s",
                "2026-10-19 20:50:00  Not answering: b (timeout)",
                "2026-10-19 21:00:00  Windows went to sleep",
                "2026-10-20 05:00:00  Windows woke up after 8 h 0 min",
                "2026-10-20 05:00:20  Not answering: a (timeout)",
                "2026-10-20 05:01:00  Ping Tray 1.2.0 started",
            };
            // Same failing hosts with another reason, all up twice, and no hosts at all add nothing;
            // the downtime runs from the first failure. Sleep ends it without "again"; checks while
            // asleep, and in the first seconds after waking, are left out. A restart ends it too.
            Equal(string.Join("\n", expected), string.Join("\n", journal.Entries()), "journal entries");
            Equal(expected.Length, heard.Count, "the window hears of every entry");

            // Tonight on the user's laptop (Kernel-Power events 506 and 507): the lid closed at
            // 20:57 with Modern Standby flipping a few times, a maintenance wake at 21:09 ran one
            // check with the network off, and the lid opened at 21:58.
            var standby = new Journal(Path.Combine(Path.GetDirectoryName(path), "standby.txt"));
            DateTime day = new DateTime(2026, 10, 19);
            standby.Record(Results(Up("ya.ru", 168)), day.Add(new TimeSpan(20, 56, 4)));
            foreach (string evening in new[] { "506 20:57:00", "507 20:57:01", "506 20:57:01", "507 20:57:03",
                "506 20:57:03", "507 21:09:36", "506 21:09:36", "check 21:09:39", "507 21:58:11", "506 21:58:11",
                "507 21:58:11", "check 21:58:25" })
            {
                string[] parts = evening.Split(' ');
                DateTime time = day.Add(TimeSpan.Parse(parts[1]));
                if (parts[0] == "506")
                    standby.Sleeping(time);
                else if (parts[0] == "507")
                    standby.WokeUp(time);
                else
                    standby.Record(Results(time.Hour == 21 && time.Minute == 9 ? Down("ya.ru", "timeout") : Up("ya.ru", 170)), time);
            }
            Equal("2026-10-19 20:57:00  Windows went to sleep\n2026-10-19 21:58:11  Windows woke up after 1 h 1 min",
                string.Join("\n", standby.Entries()), "Modern Standby: one sleep, no outage while asleep");

            for (int i = 0; i < Journal.MaxEntries + 20; i++)
                journal.Started("1.2.0", start.AddDays(1).AddMinutes(i));
            List<string> kept = journal.Entries();
            Equal(Journal.MaxEntries, kept.Count, "the file keeps the latest entries only");
            Equal(true, kept.Last().StartsWith(start.AddDays(1).AddMinutes(Journal.MaxEntries + 19).ToString("yyyy-MM-dd HH:mm")),
                "the latest entry is kept");
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        static void LinksTests()
        {
            Equal("https://github.com/t7ko/windows-ping-tray", Links.HomePage, "the home page");
            string url = Links.BugReportUrl(new Settings(), "Online\n× a: timeout");
            Equal(true, url.StartsWith("mailto:ivan.t7ko@gmail.com?subject="), "a mail to the author");
            Equal(false, url.Contains(" ") || url.Contains("\n"), "nothing is left unescaped");
            string body = Uri.UnescapeDataString(url.Substring(url.IndexOf("&body=", StringComparison.Ordinal) + 6));
            Equal(true, body.Contains("Ping Tray " + Updater.Text(Updater.CurrentVersion) + " on Windows build "),
                "the versions of Ping Tray and Windows: " + body.Split('\r')[3]);
            Equal(true, body.Contains("Checks: ping every 60 s, timeout 2000 ms; updates: auto"), "the settings");
            Equal(true, body.Contains("Hosts: ya.ru, google.com, 1.1.1.1"), "the hosts");
            Equal(true, body.Contains("Tooltip:\r\nOnline\r\n× a: timeout"), "the tooltip, with Windows line ends");
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
            Equal(60, new Settings().IntervalSeconds, "a check a minute by default");
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
            // The failure cases try once (CheckOnceAsync): with retries each would take seconds.
            HostResult missing = Pinger.CheckOnceAsync("no-such-host.invalid", new Settings()).Result;
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
            HostResult closed = Pinger.CheckOnceAsync("127.0.0.1", tcp).Result;
            Equal("refused", closed.Error, "a closed TCP port is refused");
            // 192.0.2.1 is a documentation address: nothing there answers.
            var silent = new Settings { Method = CheckMethod.Tcp, TcpPort = 443, TimeoutMs = 300 };
            HostResult nobody = Pinger.CheckOnceAsync("192.0.2.1", silent).Result;
            Equal(true, !nobody.Ok && (nobody.Error == "timeout" || nobody.Error == "unreachable"),
                "a silent TCP host times out (" + nobody.Error + ")");

            // Retries: a host that never answers is tried Tries times, RetryDelayMs apart...
            var watch = System.Diagnostics.Stopwatch.StartNew();
            HostResult dead = Pinger.CheckAsync("192.0.2.1", silent).Result;
            long deadMs = watch.ElapsedMilliseconds;
            Equal(true, !dead.Ok && deadMs >= (Pinger.Tries - 1) * Pinger.RetryDelayMs && deadMs < 10000,
                "a dead host fails after " + Pinger.Tries + " tries (" + deadMs + " ms)");
            // ...and one that comes up between tries counts as up.
            tcp.TimeoutMs = 500;
            watch.Restart();
            var late = new TcpListener(IPAddress.Loopback, port);
            System.Threading.Tasks.Task.Delay(1200).ContinueWith(t => late.Start());
            HostResult revived = Pinger.CheckAsync("127.0.0.1", tcp).Result;
            long revivedMs = watch.ElapsedMilliseconds;
            late.Stop();
            Equal(true, revived.Ok && revivedMs >= Pinger.RetryDelayMs,
                "a host that answers a later try is up (" + revived.Error + ", " + revivedMs + " ms)");
        }

        static void HoverTests()
        {
            var hover = new HoverTracker();
            var at = new System.Drawing.Point(100, 200);
            var away = new System.Drawing.Point(300, 200);
            Equal(true, hover.Moved(at, 1000), "the first mouse move starts a hover");
            Equal(false, hover.Moved(at, 1100), "more mouse moves do not start another");
            Equal(false, hover.Poll(at, 9000), "a pointer resting on the icon stays");
            Equal(false, hover.Poll(away, 1100 + HoverTracker.LeaveMs - 1), "a move without a mouse move yet: maybe still on it");
            Equal(true, hover.Poll(away, 1100 + HoverTracker.LeaveMs), "moved and no mouse move for a while: gone");
            Equal(false, hover.Hovering, "not hovering any more");
            Equal(false, hover.Poll(away, 20000), "polls after the end change nothing");
            Equal(true, hover.Moved(at, 30000), "coming back starts a new hover");

            var settings = new Settings();
            Equal("1 of 2 hosts answer (live)\n× b: timeout\na: 5 ms",
                StatusReport.Tooltip(Results(Up("a", 5), Down("b", "timeout")), settings, true), "live tooltip");
            Equal("Offline: no host answers (TCP 443, live)\n× b: timeout",
                StatusReport.Tooltip(Results(Down("b", "timeout")), new Settings { Method = CheckMethod.Tcp }, true),
                "live tooltip with TCP");
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
