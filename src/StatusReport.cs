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
        /// A summary line, then one "host: time" line per host. Lines that do not fit into a
        /// tooltip are replaced by "+N more". The summary names the TCP port when that is used.
        /// </summary>
        public static string Tooltip(IList<HostResult> results, Settings settings)
        {
            if (results.Count == 0)
                return "Ping Tray: no hosts to check.\nDouble-click to add some.";
            int up = results.Count(r => r.Ok);
            string header = up == results.Count ? "Online"
                : up == 0 ? "Offline: no host answers"
                : up + " of " + results.Count + " hosts answer";
            if (settings.Method == CheckMethod.Tcp)
                header += " (TCP " + settings.TcpPort + ")";
            List<string> lines = results.Select(Line).ToList();
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

        public static string Line(HostResult result)
        {
            string value = !result.Ok ? result.Error
                : result.RoundtripMs < 1 ? "<1 ms"
                : result.RoundtripMs + " ms";
            string line = result.Host + ": " + value;
            return line.Length <= 60 ? line : line.Substring(0, 59) + "…";
        }
    }
}
