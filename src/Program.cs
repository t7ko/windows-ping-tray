using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

// The version of a release: scripts/release.ps1 tags 1.2.3.0 as v1.2.3, and Updater compares it
// with the latest tag on GitHub.
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyTitle("Ping Tray")]
[assembly: AssemblyDescription("Shows in the notification area whether the internet is reachable")]
[assembly: AssemblyProduct("Ping Tray")]
// Without it .NET runs the exe as if built for 4.0, whose HTTPS lacks TLS 1.2 and fails on GitHub.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace PingTray
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool updated = Updater.FinishUpdate(args, Application.ExecutablePath);

            bool firstInstance;
            using (new Mutex(true, @"Local\PingTray", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Ping Tray is already running. Its icon may be hidden behind the ^ arrow " +
                        "in the notification area.", "Ping Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += (sender, e) => MessageBox.Show(e.Exception.ToString(),
                    "Ping Tray error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // So that "await" continues on this thread. WinForms would install it only with the first control.
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                TrayApp app;
                try
                {
                    app = new TrayApp(updated);
                }
                catch (Exception e)
                {
                    MessageBox.Show("Ping Tray could not start:\n" + e.Message, "Ping Tray",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Application.Run(app);
            }
        }
    }
}
