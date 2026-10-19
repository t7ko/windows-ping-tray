using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PingTray
{
    /// <summary>
    /// A short record of state changes: when hosts stop answering, and when all answer again
    /// with how long it took. Also when Ping Tray starts and when Windows goes to sleep, which
    /// explain gaps. Kept in journal.txt beside the settings, the latest MaxEntries lines.
    /// </summary>
    public class Journal
    {
        public const int MaxEntries = 500;
        const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        readonly string path;
        HashSet<string> failing;  // hosts that failed the last check; null before the first one
        DateTime downSince;       // when the hosts stopped all answering, while failing is not empty

        /// <summary>A new entry, as its line in the file.</summary>
        public event Action<string> Added;

        public Journal(string path)
        {
            this.path = path;
        }

        public static string DefaultPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Settings.FilePath), "journal.txt"); }
        }

        public string FilePath
        {
            get { return path; }
        }

        public void Started(string version, DateTime now)
        {
            failing = null;
            Add(now, "Ping Tray " + version + " started");
        }

        /// <summary>Sleep ends what is going on: a night asleep is no downtime.</summary>
        public void Sleeping(DateTime now)
        {
            failing = null;
            Add(now, "Windows went to sleep");
        }

        /// <summary>Records what a check found if the failing hosts differ from the check before.</summary>
        public void Record(IList<HostResult> results, DateTime now)
        {
            if (results.Count == 0)
                return;
            List<HostResult> failed = results.Where(r => !r.Ok).ToList();
            var hosts = new HashSet<string>(failed.Select(r => r.Host));
            if (failing != null && hosts.SetEquals(failing))
                return;
            bool wasDown = failing != null && failing.Count > 0;
            if (hosts.Count == 0)
            {
                if (wasDown)
                    Add(now, "Online again after " + Duration(now - downSince));
            }
            else
            {
                if (!wasDown)
                    downSince = now;
                Add(now, failed.Count == results.Count ? "Offline: no host answers"
                    : "Not answering: " + string.Join(", ", failed.Select(r => r.Host + " (" + r.Error + ")")));
            }
            failing = hosts;
        }

        /// <summary>The entries in the file, oldest first; none if it cannot be read.</summary>
        public List<string> Entries()
        {
            try
            {
                return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).Where(l => l.Length > 0).ToList()
                    : new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>"45 s", "2 min 12 s", "3 h 5 min", "2 d 4 h".</summary>
        public static string Duration(TimeSpan time)
        {
            if (time.TotalSeconds < 60)
                return Math.Max(0, (int)time.TotalSeconds) + " s";
            if (time.TotalMinutes < 60)
                return (int)time.TotalMinutes + " min " + time.Seconds + " s";
            if (time.TotalHours < 24)
                return (int)time.TotalHours + " h " + time.Minutes + " min";
            return (int)time.TotalDays + " d " + time.Hours + " h";
        }

        void Add(DateTime time, string text)
        {
            string line = time.ToString(TimeFormat, CultureInfo.InvariantCulture) + "  " + text;
            try
            {
                List<string> lines = Entries();
                lines.Add(line);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Count - MaxEntries)), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // The file is a convenience; the window still gets the entry.
            }
            if (Added != null)
                Added(line);
        }
    }
}
