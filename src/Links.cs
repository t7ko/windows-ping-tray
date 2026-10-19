using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PingTray
{
    /// <summary>
    /// The home page, and "Report a problem": a new mail to the author in the user's mail app,
    /// with the facts that help already filled in. The user adds what happened and sends it.
    /// </summary>
    public static class Links
    {
        public const string HomePage = "https://github.com/" + Updater.Repository;
        public const string BugAddress = "ivan.t7ko@gmail.com";

        public static void OpenHomePage()
        {
            try
            {
                using (Process.Start(HomePage)) { }
            }
            catch (Exception)
            {
                MessageBox.Show("No web browser could open " + HomePage + ".", "Ping Tray",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static void ReportProblem(Settings settings, string tooltip)
        {
            try
            {
                using (Process.Start(BugReportUrl(settings, tooltip))) { }
            }
            catch (Exception)
            {
                try
                {
                    Clipboard.SetText(BugAddress);
                }
                catch (Exception)
                {
                    // the clipboard is busy; the address is in the message anyway
                }
                MessageBox.Show("No mail app could open a message. Please write to " + BugAddress +
                    " (it is on the clipboard): what happened, what you expected, and that this is Ping Tray " +
                    Updater.Text(Updater.CurrentVersion) + ".", "Ping Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static string BugReportUrl(Settings settings, string tooltip)
        {
            string version = Updater.Text(Updater.CurrentVersion);
            var body = new StringBuilder();
            body.Append("What happened, and what did you expect?\r\n\r\n\r\n");
            body.Append("Ping Tray " + version + " on Windows " + WindowsVersion() + "\r\n");
            body.Append("Checks: " + (settings.Method == CheckMethod.Tcp ? "TCP port " + settings.TcpPort : "ping") +
                " every " + settings.IntervalSeconds + " s, timeout " + settings.TimeoutMs + " ms; updates: " +
                settings.Updates.ToString().ToLowerInvariant() + "\r\n");
            body.Append("Hosts: " + string.Join(", ", settings.Hosts) + "\r\n");
            body.Append("Tooltip:\r\n" + tooltip.Replace("\n", "\r\n") + "\r\n");
            return "mailto:" + BugAddress + "?subject=" + Uri.EscapeDataString("Ping Tray " + version + ": a problem") +
                "&body=" + Uri.EscapeDataString(body.ToString());
        }

        /// <summary>
        /// Such as "build 26300.6899 (25H2)", from the registry: Environment.OSVersion says 6.2
        /// (Windows 8) to any program whose manifest does not declare Windows 10.
        /// </summary>
        static string WindowsVersion()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    object build = key.GetValue("CurrentBuildNumber"), patch = key.GetValue("UBR"),
                        release = key.GetValue("DisplayVersion");
                    if (build != null)
                        return "build " + build + (patch != null ? "." + patch : "") +
                            (release != null ? " (" + release + ")" : "");
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return Environment.OSVersion.Version.ToString();
        }
    }
}
