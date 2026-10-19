using System;
using System.Collections.Generic;
using System.Linq;

namespace PingTray
{
    /// <summary>Turns ping results into the icon color and the tooltip text.</summary>
    public static class StatusReport
    {
        /// <summary>NOTIFYICONDATA.szTip holds 128 characters, including the terminating zero.</summary>
        public const int MaxTooltipLength = 127;

        public static Status Overall(IList<HostResult> results)
        {
            if (results.Count == 0)
                return Status.Unknown;
            int up = results.Count(r => r.Ok);
            return up == results.Count ? Status.Good : up == 0 ? Status.Down : Status.Partial;
        }

        /// <summary>
        /// A summary line, then one line per host: first the hosts that fail, marked with ×, in
        /// the order of the settings; then the others from the slowest to the fastest. Lines that
        /// do not fit into a tooltip are replaced by "+N more", so the fastest hosts go first and
        /// failing ones stay. The summary names the TCP port when that is used, and says "live"
        /// while the mouse is over the icon and failing hosts are pinged every second.
        /// </summary>
        public static string Tooltip(IList<HostResult> results, Settings settings, bool live = false)
        {
            if (results.Count == 0)
                return "Ping Tray: no hosts to check.\nDouble-click to add some.";
            int up = results.Count(r => r.Ok);
            string header = up == results.Count ? "Online"
                : up == 0 ? "Offline: no host answers"
                : up + " of " + results.Count + " hosts answer";
            var tags = new List<string>();
            if (settings.Method == CheckMethod.Tcp)
                tags.Add("TCP " + settings.TcpPort);
            if (live)
                tags.Add("live");
            if (tags.Count > 0)
                header += " (" + string.Join(", ", tags) + ")";
            // OrderByDescending is stable: hosts with the same time keep the order of the settings.
            List<string> lines = results.Where(r => !r.Ok)
                .Concat(results.Where(r => r.Ok).OrderByDescending(r => r.RoundtripMs))
                .Select(Line).ToList();
            for (int shown = lines.Count; shown > 0; shown--)
            {
                string text = header + string.Concat(lines.Take(shown).Select(line => "\n" + line));
                if (shown < lines.Count)
                    text += "\n+" + (lines.Count - shown) + " more";
                if (text.Length <= MaxTooltipLength)
                    return text;
            }
            return header + "\n+" + lines.Count + " hosts";
        }

        /// <summary>
        /// The multiplication sign marks failing hosts: unlike ✗ it is in every Windows font, so
        /// the tooltip shows it whatever font it uses.
        /// </summary>
        public const string FailedMark = "× ";

        public static string Line(HostResult result)
        {
            string line = !result.Ok ? FailedMark + result.Host + ": " + result.Error
                : result.Host + ": " + (result.RoundtripMs < 1 ? "<1 ms" : result.RoundtripMs + " ms");
            return line.Length <= 60 ? line : line.Substring(0, 59) + "…";
        }
    }
}
