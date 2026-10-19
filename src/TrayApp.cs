using System;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PingTray
{
    /// <summary>The tray icon. Checks the hosts on a timer and shows the result as its color and tooltip.</summary>
    class TrayApp : ApplicationContext
    {
        readonly SynchronizationContext ui;
        readonly Icon[] icons;  // indexed by Status
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer pingTimer;
        readonly System.Windows.Forms.Timer networkChangeTimer;
        readonly System.Windows.Forms.Timer updateTimer;
        readonly ToolStripMenuItem updateItem;
        Settings settings;
        SettingsForm settingsForm;
        bool checking, checkAgain, exiting;
        Release update;  // a newer release, found but not installed yet
        DateTime lastUpdateCheck = DateTime.MinValue;  // UTC
        bool checkingUpdate, installing;
        string tooltip = "";  // as shown, for problem reports

        /// <param name="updated">Whether an update has just started this instance.</param>
        public TrayApp(bool updated)
        {
            ui = SynchronizationContext.Current;
            settings = Settings.Load();
            int size = SystemInformation.SmallIconSize.Width;
            icons = Enum.GetValues(typeof(Status)).Cast<Status>()
                .Select(status => StatusIcons.CreateIcon(status, size)).ToArray();

            var settingsItem = new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);  // what a double-click does
            updateItem = new ToolStripMenuItem("Check for updates", null, delegate { OnUpdateItem(); });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Check connection now", null, delegate { Check(true); });
            menu.Items.Add(settingsItem);
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Home page", null, delegate { Links.OpenHomePage(); });
            menu.Items.Add("Report a problem…", null, delegate { ReportProblem(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon { Icon = icons[(int)Status.Unknown], ContextMenuStrip = menu };
            SetTooltip("Ping Tray: checking…");
            tray.MouseDoubleClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.BalloonTipClicked += delegate
            {
                if (update != null)
                    InstallUpdate(true);
            };
            tray.Visible = true;
            if (updated)
                tray.ShowBalloonTip(5000, "Ping Tray updated",
                    "This is version " + Updater.Text(Updater.CurrentVersion) + ".", ToolTipIcon.Info);

            pingTimer = new System.Windows.Forms.Timer { Interval = settings.IntervalSeconds * 1000 };
            pingTimer.Tick += delegate { Check(false); };
            pingTimer.Start();

            // When Wi-Fi drops or a cable is plugged in, the addresses change: check soon instead of
            // waiting for the next tick. The event comes several times per change, on another thread.
            networkChangeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            networkChangeTimer.Tick += delegate
            {
                networkChangeTimer.Stop();
                Check(true);
            };
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // The first update check waits a minute, so that it does not slow down logging in.
            // Then the timer ticks hourly, and CheckForUpdateDaily goes to GitHub once a day.
            updateTimer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 60 * 60 * 1000;
                CheckForUpdateDaily();
            };
            updateTimer.Start();

            Check(true);
        }

        void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            ui.Post(delegate
            {
                if (exiting)
                    return;
                networkChangeTimer.Stop();
                networkChangeTimer.Start();
            }, null);
        }

        /// <summary>
        /// Checks all hosts at once and shows the results. While a check runs, a timer tick is
        /// dropped; other requests (queue = true) run another check after it.
        /// </summary>
        async void Check(bool queue)
        {
            if (checking)
            {
                checkAgain |= queue;
                return;
            }
            checking = true;
            try
            {
                do
                {
                    checkAgain = false;
                    Settings used = settings;
                    HostResult[] results = await Task.WhenAll(used.Hosts.Select(host =>
                        Task.Run(() => Pinger.CheckAsync(host, used))));
                    if (exiting)
                        return;
                    if (used == settings)
                        Show(results);
                    else
                        checkAgain = true;  // the hosts were changed meanwhile
                } while (checkAgain);
            }
            finally
            {
                checking = false;
            }
        }

        void Show(HostResult[] results)
        {
            tray.Icon = icons[(int)StatusReport.Overall(results)];
            SetTooltip(StatusReport.Tooltip(results, settings));
        }

        /// <summary>
        /// NotifyIcon.Text refuses more than 63 characters on .NET Framework, though Windows shows
        /// 127, so this sets its private field instead. The field and method are stable: .NET
        /// Framework 4.8 gets no new features.
        /// </summary>
        void SetTooltip(string text)
        {
            if (text.Length > StatusReport.MaxTooltipLength)
                text = text.Substring(0, StatusReport.MaxTooltipLength);
            tooltip = text;
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo textField = typeof(NotifyIcon).GetField("text", hidden);
            FieldInfo addedField = typeof(NotifyIcon).GetField("added", hidden);
            MethodInfo updateIcon = typeof(NotifyIcon).GetMethod("UpdateIcon", hidden);
            if (textField != null && addedField != null && updateIcon != null)
            {
                textField.SetValue(tray, text);
                if ((bool)addedField.GetValue(tray))
                    updateIcon.Invoke(tray, new object[] { tray.Visible });
            }
            else
                tray.Text = text.Length <= 63 ? text : text.Substring(0, 62) + "…";
        }

        void ShowSettings()
        {
            if (settingsForm == null)
            {
                settingsForm = new SettingsForm(settings, ReportProblem);
                settingsForm.Saved += ApplySettings;
                settingsForm.FormClosed += delegate { settingsForm = null; };
                settingsForm.Show();
            }
            settingsForm.Activate();
        }

        void ReportProblem()
        {
            Links.ReportProblem(settings, tooltip);
        }

        void ApplySettings(Settings saved)
        {
            settings = saved;
            pingTimer.Interval = settings.IntervalSeconds * 1000;  // also restarts the countdown
            Check(true);
            CheckForUpdateDaily();  // in case updates were just turned on
        }

        /// <summary>Once a day, unless updates are off: installs a newer release, or offers it.</summary>
        async void CheckForUpdateDaily()
        {
            if (settings.Updates == UpdateMode.Off || checkingUpdate || installing
                || DateTime.UtcNow - lastUpdateCheck < TimeSpan.FromHours(23))
                return;
            checkingUpdate = true;
            Release found;
            try
            {
                found = await Task.Run(() => Updater.FindUpdateAsync());
            }
            catch (Exception)
            {
                return;  // offline, or GitHub is unreachable: the next hourly tick tries again
            }
            finally
            {
                checkingUpdate = false;
            }
            lastUpdateCheck = DateTime.UtcNow;
            if (found == null || exiting)
                return;
            bool offeredBefore = update != null && update.Version == found.Version;
            Offer(found);
            if (settings.Updates == UpdateMode.Auto)
                InstallUpdate(false);
            else if (!offeredBefore)
                tray.ShowBalloonTip(10000, "Ping Tray " + Updater.Text(found.Version) + " is available",
                    "Click here to install it.", ToolTipIcon.Info);
        }

        /// <summary>The menu's update item: installs the release on offer, or else checks GitHub now.</summary>
        async void OnUpdateItem()
        {
            if (update != null)
            {
                InstallUpdate(true);
                return;
            }
            if (checkingUpdate || installing)
                return;
            checkingUpdate = true;
            Release found;
            try
            {
                found = await Task.Run(() => Updater.FindUpdateAsync());
            }
            catch (Exception e)
            {
                MessageBox.Show("Could not check for updates:\n" + e.Message, "Ping Tray",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                checkingUpdate = false;
            }
            lastUpdateCheck = DateTime.UtcNow;
            string current = Updater.Text(Updater.CurrentVersion);
            if (found == null)
            {
                MessageBox.Show("Ping Tray " + current + " is the latest version.", "Ping Tray",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Offer(found);
            string question = "Ping Tray " + Updater.Text(found.Version) + " is available; this is " + current +
                ".\n\nInstall it now? Ping Tray restarts.";
            if (MessageBox.Show(question, "Ping Tray", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                InstallUpdate(true);
        }

        void Offer(Release release)
        {
            update = release;
            updateItem.Text = "Install version " + Updater.Text(release.Version);
        }

        /// <summary>
        /// Downloads the release on offer, puts it in place of this exe, starts it and exits.
        /// Errors go to a message box when the user asked for the update, otherwise to a balloon.
        /// </summary>
        async void InstallUpdate(bool userAsked)
        {
            if (installing || update == null)
                return;
            installing = true;
            Release release = update;
            string exe = Application.ExecutablePath;
            try
            {
                string downloaded = await Task.Run(() => Updater.DownloadAsync(release, exe));
                if (exiting)
                    return;
                Updater.Install(exe, downloaded);
            }
            catch (Exception e)
            {
                installing = false;
                string reason = e is UnauthorizedAccessException
                    ? "Windows does not let it replace " + exe + ". Move PingTray.exe to a folder " +
                        "you can write to, such as %LOCALAPPDATA%\\Programs."
                    : e.Message;
                if (userAsked)
                    MessageBox.Show("Could not update Ping Tray:\n" + reason, "Ping Tray",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    tray.ShowBalloonTip(10000, "Ping Tray could not update itself", reason, ToolTipIcon.Warning);
                return;
            }
            ExitThread();  // the new version waits for this process to end
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            pingTimer.Stop();
            networkChangeTimer.Stop();
            updateTimer.Stop();
            if (settingsForm != null)
                settingsForm.Close();
            tray.Visible = false;  // otherwise the icon stays until the mouse passes over it
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
