using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Ping Tray")]
[assembly: AssemblyDescription("Shows in the notification area whether the internet is reachable")]
[assembly: AssemblyProduct("Ping Tray")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PingTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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
                    app = new TrayApp();
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
