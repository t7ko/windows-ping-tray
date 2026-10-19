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
        readonly System.Windows.Forms.Timer wakeTimer;
        readonly System.Windows.Forms.Timer hoverTimer;
        readonly HoverTracker hover = new HoverTracker();
        readonly ToolStripMenuItem updateItem;
        readonly Journal journal = new Journal(Journal.DefaultPath);
        readonly SleepWatcher sleepWatcher;
        Settings settings;
        SettingsForm settingsForm;
        JournalForm journalForm;
        bool checking, checkAgain, exiting;
        int userChecks;  // checks the user asked for; the icon is gray until one started after the last ends
        bool grayed;     // gray for such a check right now
        HostResult[] shown;  // on display: the last check's results, with live pings merged in
        const int LiveIntervalMs = 1000;
        static readonly TimeSpan MaxLive = TimeSpan.FromMinutes(10);
        bool live;
        int liveRound;   // live ping loops run while this is the round they started in
        DateTime liveSince;  // UTC
        Release update;  // a newer release, found but not installed yet
        DateTime lastUpdateCheck = DateTime.MinValue;  // UTC
        bool checkingUpdate, installing;
        string tooltip = "";  // as shown, for problem reports

        /// <param name="updated">Whether an update has just started this instance.</param>
        public TrayApp(bool updated)
        {
            ui = SynchronizationContext.Current;
            settings = Settings.Load();
            journal.Started(Updater.Text(Updater.CurrentVersion), DateTime.Now);
            int size = SystemInformation.SmallIconSize.Width;
            icons = Enum.GetValues(typeof(Status)).Cast<Status>()
                .Select(status => StatusIcons.CreateIcon(status, size)).ToArray();

            var settingsItem = new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);  // what a double-click does
            updateItem = new ToolStripMenuItem("Check for updates", null, delegate { OnUpdateItem(); });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Check connection now", null, delegate { CheckNow(); });
            menu.Items.Add(settingsItem);
            menu.Items.Add("Journal…", null, delegate { ShowJournal(); });
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Home page", null, delegate { Links.OpenHomePage(); });
            menu.Items.Add("Report a problem…", null, delegate { ReportProblem(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon { Icon = icons[(int)Status.Unknown], ContextMenuStrip = menu };
            SetTooltip("Ping Tray: checking…");
            // The first click of a double-click also arrives as a click, so a double-click checks too.
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) CheckNow(); };
            tray.MouseDoubleClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.MouseMove += delegate { OnTrayMouseMove(); };
            hoverTimer = new System.Windows.Forms.Timer { Interval = 200 };
            hoverTimer.Tick += delegate { OnHoverTick(); };
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

            // A wake goes into the journal once Windows has stayed awake a while (Journal.Settle);
            // this timer makes sure that happens even if no check comes first.
            wakeTimer = new System.Windows.Forms.Timer { Interval = (Journal.WakeSettleSeconds + 1) * 1000 };
            wakeTimer.Tick += delegate
            {
                wakeTimer.Stop();
                journal.Settle(DateTime.Now);
            };
            sleepWatcher = new SleepWatcher((sleeping, time) => ui.Post(delegate { OnSleepChanged(sleeping, time); }, null));

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

        void OnSleepChanged(bool sleeping, DateTime time)
        {
            if (exiting)
                return;
            if (sleeping)
                journal.Sleeping(time);
            else
            {
                journal.WokeUp(time);
                wakeTimer.Stop();
                wakeTimer.Start();
            }
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
        /// A check the user asked for (left click, or the menu). The icon turns gray until it is
        /// done, so that the user sees it ran even when the color comes back the same.
        /// </summary>
        void CheckNow()
        {
            userChecks++;
            grayed = true;
            tray.Icon = icons[(int)Status.Unknown];
            Check(true);
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
                    int userChecksBefore = userChecks;
                    HostResult[] results = await Task.WhenAll(used.Hosts.Select(host =>
                        Task.Run(() => Pinger.CheckAsync(host, used))));
                    if (exiting)
                        return;
                    if (used == settings)
                        // A click during this check queued another one: the icon stays gray until then.
                        Show(results, userChecks == userChecksBefore);
                    else
                        checkAgain = true;  // the hosts were changed meanwhile
                } while (checkAgain);
            }
            finally
            {
                checking = false;
            }
        }

        void Show(HostResult[] results, bool color)
        {
            shown = results;
            if (color)
            {
                tray.Icon = icons[(int)StatusReport.Overall(results)];
                grayed = false;
            }
            SetTooltip(StatusReport.Tooltip(results, settings, live));
            journal.Record(results, DateTime.Now);
        }

        void OnTrayMouseMove()
        {
            if (!hover.Moved(Cursor.Position, Environment.TickCount))
                return;
            hoverTimer.Start();
            if (shown != null && shown.Any(r => !r.Ok))
                StartLive();
        }

        void OnHoverTick()
        {
            if (hover.Poll(Cursor.Position, Environment.TickCount))
            {
                hoverTimer.Stop();
                StopLive();
            }
            else if (live && DateTime.UtcNow - liveSince > MaxLive)
                StopLive();  // a pointer left resting on the icon should not ping all night
        }

        /// <summary>
        /// While the mouse is over an icon that is not green: pings each failing host every
        /// second, like ping.exe, and shows each answer at once. These single pings are not
        /// retried and stay out of the journal.
        /// </summary>
        void StartLive()
        {
            live = true;
            liveSince = DateTime.UtcNow;
            int round = ++liveRound;
            foreach (HostResult result in shown.Where(r => !r.Ok))
                PingLive(result.Host, round);
            SetTooltip(StatusReport.Tooltip(shown, settings, true));
        }

        /// <summary>Ends live pinging; a normal check, with retries, then settles the state.</summary>
        void StopLive()
        {
            if (!live)
                return;
            live = false;
            liveRound++;
            SetTooltip(StatusReport.Tooltip(shown, settings));
            Check(true);
        }

        async void PingLive(string host, int round)
        {
            Settings used = settings;
            while (round == liveRound && used == settings && !exiting)
            {
                int started = Environment.TickCount;
                HostResult result = await Task.Run(() => Pinger.CheckOnceAsync(host, used));
                if (round != liveRound || used != settings || exiting)
                    return;
                shown = shown.Select(r => r.Host == host ? result : r).ToArray();
                if (!grayed)
                    tray.Icon = icons[(int)StatusReport.Overall(shown)];
                SetTooltip(StatusReport.Tooltip(shown, settings, true));
                int wait = LiveIntervalMs - unchecked(Environment.TickCount - started);
                if (wait > 0)
                    await Task.Delay(wait);
            }
        }

        void ShowJournal()
        {
            if (journalForm == null)
            {
                journalForm = new JournalForm(journal);
                journalForm.FormClosed += delegate { journalForm = null; };
                journalForm.Show();
            }
            journalForm.Activate();
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
            sleepWatcher.Dispose();
            wakeTimer.Stop();
            hoverTimer.Stop();
            liveRound++;
            pingTimer.Stop();
            networkChangeTimer.Stop();
            updateTimer.Stop();
            if (settingsForm != null)
                settingsForm.Close();
            if (journalForm != null)
                journalForm.Close();
            tray.Visible = false;  // otherwise the icon stays until the mouse passes over it
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
