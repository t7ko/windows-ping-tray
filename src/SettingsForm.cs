using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PingTray
{
    /// <summary>Edits the hosts, the interval, the timeout, the check method, updates and "Start with Windows".</summary>
    class SettingsForm : Form
    {
        public event Action<Settings> Saved;

        readonly TextBox hostsBox;
        readonly NumericUpDown intervalBox, timeoutBox, portBox;
        readonly RadioButton tcpButton;
        readonly ComboBox updatesBox;
        readonly CheckBox autostartBox;
        readonly bool autostartWas;

        /// <param name="reportProblem">Opens a problem report (it needs the tray's tooltip).</param>
        public SettingsForm(Settings settings, Action reportProblem)
        {
            Text = "Ping Tray " + Updater.Text(Updater.CurrentVersion) + " settings";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            int em = Font.Height;  // sizes below are in em, so they grow with the display scale

            hostsBox = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(20 * em, 8 * em),
                Margin = new Padding(3, 3, 3, em / 2),
                Text = string.Join("\r\n", settings.Hosts),
            };
            intervalBox = Number(Settings.MinIntervalSeconds, Settings.MaxIntervalSeconds, 1, settings.IntervalSeconds, em);
            timeoutBox = Number(Settings.MinTimeoutMs, Settings.MaxTimeoutMs, 100, settings.TimeoutMs, em);

            // Radio buttons are exclusive within their parent, so both go into one panel.
            var pingButton = new RadioButton
            {
                Text = "Ping (ICMP echo)",
                AutoSize = true,
                Checked = settings.Method == CheckMethod.Ping,
            };
            tcpButton = new RadioButton
            {
                Text = "Connect to TCP port",
                AutoSize = true,
                Checked = settings.Method == CheckMethod.Tcp,
                Anchor = AnchorStyles.Left,
            };
            portBox = Number(Settings.MinPort, Settings.MaxPort, 1, settings.TcpPort, em);
            portBox.Enabled = tcpButton.Checked;
            tcpButton.CheckedChanged += delegate { portBox.Enabled = tcpButton.Checked; };
            var methodPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            AddWide(methodPanel, pingButton);
            AddRow(methodPanel, tcpButton, portBox);
            var hint = new Label
            {
                Text = "TCP is for networks that block ping. Web servers accept port 443 (HTTPS).",
                AutoSize = true,
                MaximumSize = new Size(hostsBox.Width, 0),
                ForeColor = SystemColors.GrayText,
            };

            updatesBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 12 * em,
                Anchor = AnchorStyles.Left,
            };
            updatesBox.Items.AddRange(new object[] { "Install automatically", "Ask before installing", "Don't check" });  // in UpdateMode order
            updatesBox.SelectedIndex = (int)settings.Updates;

            autostartWas = Autostart.IsEnabled;
            autostartBox = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Checked = autostartWas,
                Margin = new Padding(3, em / 2, 3, 3),
            };

            var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(5 * em, 0) };
            save.Click += OnSave;
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(5 * em, 0) };
            cancel.Click += delegate { Close(); };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, em, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(em, em),
            };
            AddWide(layout, Caption("Hosts to check, one per line (names or IP addresses):"));
            AddWide(layout, hostsBox);
            AddRow(layout, Caption("Check every"), intervalBox, Caption("seconds"));
            AddRow(layout, Caption("Timeout"), timeoutBox, Caption("ms (per host)"));
            Label checkBy = Caption("Check each host by:");
            checkBy.Margin = new Padding(3, em / 2, 3, 0);
            AddWide(layout, checkBy);
            AddWide(layout, methodPanel);
            AddWide(layout, hint);
            Label updatesLabel = Caption("Updates");
            AddRow(layout, updatesLabel, updatesBox);
            layout.SetColumnSpan(updatesBox, 2);
            updatesLabel.Margin = updatesBox.Margin = new Padding(3, em / 2, 3, 3);
            AddWide(layout, autostartBox);
            string homePage = Links.HomePage.Replace("https://", "");
            LinkLabel homeLink = Link("Home page: " + homePage, homePage, Links.OpenHomePage);
            homeLink.Margin = new Padding(3, em, 3, 0);
            AddWide(layout, homeLink);
            AddWide(layout, Link("Found a problem? Write to " + Links.BugAddress, Links.BugAddress, reportProblem));
            AddWide(layout, buttons);
            Controls.Add(layout);

            Size preferred = layout.PreferredSize;
            ClientSize = new Size(preferred.Width + 2 * em, preferred.Height + 2 * em);
            AcceptButton = save;
            CancelButton = cancel;
            // The focused text box starts with all of its text selected; one keypress would replace it.
            Shown += delegate { hostsBox.Select(hostsBox.TextLength, 0); };
        }

        void OnSave(object sender, EventArgs e)
        {
            var hosts = new List<string>();
            foreach (string line in hostsBox.Lines)
            {
                string host = HostNames.Normalize(line);
                if (host.Length == 0 || hosts.Contains(host))
                    continue;
                if (!HostNames.IsValid(host))
                {
                    MessageBox.Show(this, "\"" + line.Trim() + "\" is not a host name or an IP address.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    hostsBox.Focus();
                    return;
                }
                hosts.Add(host);
            }
            var settings = new Settings
            {
                Hosts = hosts,
                IntervalSeconds = (int)intervalBox.Value,
                TimeoutMs = (int)timeoutBox.Value,
                Method = tcpButton.Checked ? CheckMethod.Tcp : CheckMethod.Ping,
                TcpPort = (int)portBox.Value,
                Updates = (UpdateMode)updatesBox.SelectedIndex,
            };
            try
            {
                settings.Save();
                if (autostartBox.Checked != autostartWas)
                    Autostart.Set(autostartBox.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings:\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (Saved != null)
                Saved(settings);
            Close();
        }

        /// <summary>A label whose last words, linkText, are a link.</summary>
        static LinkLabel Link(string text, string linkText, Action open)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
            link.LinkArea = new LinkArea(text.Length - linkText.Length, linkText.Length);
            link.LinkClicked += delegate { open(); };
            return link;
        }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
        }

        static NumericUpDown Number(int min, int max, int step, int value, int em)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = step,
                Value = Math.Max(min, Math.Min(max, value)),
                Width = 5 * em,
                TextAlign = HorizontalAlignment.Right,
                Anchor = AnchorStyles.Left,
            };
        }

        static void AddWide(TableLayoutPanel layout, Control control)
        {
            layout.Controls.Add(control, 0, layout.RowCount);
            layout.SetColumnSpan(control, layout.ColumnCount);
            layout.RowCount++;
        }

        static void AddRow(TableLayoutPanel layout, params Control[] controls)
        {
            for (int column = 0; column < controls.Length; column++)
                layout.Controls.Add(controls[column], column, layout.RowCount);
            layout.RowCount++;
        }
    }
}
