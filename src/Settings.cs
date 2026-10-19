using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PingTray
{
    /// <summary>How a host is checked: an ICMP echo, or a TCP connection for networks that block ping.</summary>
    public enum CheckMethod
    {
        Ping,
        Tcp
    }

    /// <summary>What to check and how often. Stored as key=value lines in %APPDATA%\PingTray\settings.ini.</summary>
    public class Settings
    {
        public const int MinIntervalSeconds = 1, MaxIntervalSeconds = 3600;
        public const int MinTimeoutMs = 100, MaxTimeoutMs = 10000;
        public const int MinPort = 1, MaxPort = 65535;

        public List<string> Hosts = new List<string> { "ya.ru", "google.com", "1.1.1.1" };
        public int IntervalSeconds = 10;
        public int TimeoutMs = 2000;
        public CheckMethod Method = CheckMethod.Ping;
        public int TcpPort = 443;  // used when Method is Tcp

        public static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PingTray", "settings.ini");
            }
        }

        /// <summary>The saved settings, or the defaults when nothing has been saved yet.</summary>
        public static Settings Load()
        {
            string path = FilePath;
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new Settings();
        }

        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Format(), new UTF8Encoding(false));
        }

        /// <summary>Reads the file format. Unknown keys and bad numbers are skipped.</summary>
        public static Settings Parse(string text)
        {
            var settings = new Settings();
            settings.Hosts.Clear();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                int equals = line.IndexOf('=');
                if (line.StartsWith("#") || equals < 0)
                    continue;
                string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                string value = line.Substring(equals + 1).Trim();
                int number;
                bool isNumber = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                switch (key)
                {
                    case "host":
                        string host = HostNames.Normalize(value);
                        if (host.Length > 0 && !settings.Hosts.Contains(host))
                            settings.Hosts.Add(host);
                        break;
                    case "interval_seconds":
                        if (isNumber)
                            settings.IntervalSeconds = Clamp(number, MinIntervalSeconds, MaxIntervalSeconds);
                        break;
                    case "timeout_ms":
                        if (isNumber)
                            settings.TimeoutMs = Clamp(number, MinTimeoutMs, MaxTimeoutMs);
                        break;
                    case "method":
                        if (value.Equals("tcp", StringComparison.OrdinalIgnoreCase))
                            settings.Method = CheckMethod.Tcp;
                        break;
                    case "tcp_port":
                        if (isNumber)
                            settings.TcpPort = Clamp(number, MinPort, MaxPort);
                        break;
                }
            }
            return settings;
        }

        public string Format()
        {
            var text = new StringBuilder();
            text.AppendLine("# Ping Tray settings. Change them in the Settings window (double-click the tray icon),");
            text.AppendLine("# or edit this file and restart Ping Tray.");
            text.AppendLine("interval_seconds=" + IntervalSeconds.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("timeout_ms=" + TimeoutMs.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("# method: ping, or tcp to connect to tcp_port instead (for networks that block ping)");
            text.AppendLine("method=" + (Method == CheckMethod.Tcp ? "tcp" : "ping"));
            text.AppendLine("tcp_port=" + TcpPort.ToString(CultureInfo.InvariantCulture));
            foreach (string host in Hosts)
                text.AppendLine("host=" + host);
            return text.ToString();
        }

        static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }

    public static class HostNames
    {
        /// <summary>
        /// Turns what a user may paste ("https://Ya.ru/", "google.com:443", "[::1]") into a host
        /// name or IP address. Returns "" for a blank line.
        /// </summary>
        public static string Normalize(string text)
        {
            string host = text.Trim();
            int scheme = host.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
                host = host.Substring(scheme + 3);
            int path = host.IndexOfAny(new[] { '/', '?', '#' });
            if (path >= 0)
                host = host.Substring(0, path);
            if (host.StartsWith("["))
            {
                int end = host.IndexOf(']');
                host = end > 0 ? host.Substring(1, end - 1) : host.Substring(1);
            }
            else if (host.Count(c => c == ':') == 1)  // host:port; an IPv6 address has more colons
                host = host.Substring(0, host.IndexOf(':'));
            return host.TrimEnd('.').ToLowerInvariant();
        }

        public static bool IsValid(string host)
        {
            return host.Length > 0 && Uri.CheckHostName(ToAscii(host)) != UriHostNameType.Unknown;
        }

        /// <summary>The DNS form of an internationalized name ("пример.рф" becomes "xn--e1afmkfd.xn--p1ai").</summary>
        public static string ToAscii(string host)
        {
            try
            {
                return new IdnMapping().GetAscii(host);
            }
            catch (ArgumentException)
            {
                return host;
            }
        }
    }
}
