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
        Settings settings;
        SettingsForm settingsForm;
        bool checking, checkAgain, exiting;

        public TrayApp()
        {
            ui = SynchronizationContext.Current;
            settings = Settings.Load();
            int size = SystemInformation.SmallIconSize.Width;
            icons = Enum.GetValues(typeof(Status)).Cast<Status>()
                .Select(status => StatusIcons.CreateIcon(status, size)).ToArray();

            var settingsItem = new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);  // what a double-click does
            var menu = new ContextMenuStrip();
            menu.Items.Add("Check now", null, delegate { Check(true); });
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon { Icon = icons[(int)Status.Unknown], ContextMenuStrip = menu };
            SetTooltip("Ping Tray: checking…");
            tray.MouseDoubleClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
            tray.Visible = true;

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
                settingsForm = new SettingsForm(settings);
                settingsForm.Saved += ApplySettings;
                settingsForm.FormClosed += delegate { settingsForm = null; };
                settingsForm.Show();
            }
            settingsForm.Activate();
        }

        void ApplySettings(Settings saved)
        {
            settings = saved;
            pingTimer.Interval = settings.IntervalSeconds * 1000;  // also restarts the countdown
            Check(true);
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            pingTimer.Stop();
            networkChangeTimer.Stop();
            if (settingsForm != null)
                settingsForm.Close();
            tray.Visible = false;  // otherwise the icon stays until the mouse passes over it
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
