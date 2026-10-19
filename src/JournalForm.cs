using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PingTray
{
    /// <summary>The journal, newest entry first; entries added while it is open appear at the top.</summary>
    class JournalForm : Form
    {
        readonly Journal journal;
        readonly TextBox entriesBox;

        public JournalForm(Journal journal)
        {
            this.journal = journal;
            Text = "Ping Tray journal";
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            int em = Font.Height;

            entriesBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BackColor = SystemColors.Window,
                Dock = DockStyle.Fill,
                Text = string.Join("\r\n", Enumerable.Reverse(journal.Entries())),
            };
            var openFile = new Button { Text = "Open file", AutoSize = true, MinimumSize = new Size(5 * em, 0) };
            openFile.Click += delegate { OpenFile(); };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(5 * em, 0) };
            close.Click += delegate { Close(); };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, em / 2, 0, 0),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(openFile);

            Padding = new Padding(em);
            Controls.Add(entriesBox);
            Controls.Add(buttons);
            ClientSize = new Size(34 * em, 20 * em);
            CancelButton = close;

            journal.Added += OnAdded;
            FormClosed += delegate { journal.Added -= OnAdded; };
            // The read-only box starts with all of its text selected otherwise.
            Shown += delegate { entriesBox.Select(0, 0); };
        }

        void OnAdded(string line)
        {
            entriesBox.Text = entriesBox.TextLength == 0 ? line : line + "\r\n" + entriesBox.Text;
        }

        void OpenFile()
        {
            try
            {
                using (Process.Start(journal.FilePath)) { }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Could not open " + journal.FilePath + ":\n" + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
