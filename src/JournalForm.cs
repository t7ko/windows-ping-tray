using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PingTray
{
    /// <summary>
    /// The journal, oldest entry first. It opens scrolled to the end, and entries added while it
    /// is open appear there.
    /// </summary>
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
                Text = string.Join("\r\n", journal.Entries()),
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
            ClientSize = new Size(44 * em, 20 * em);
            CancelButton = close;

            journal.Added += OnAdded;
            FormClosed += delegate { journal.Added -= OnAdded; };
            // Without this the read-only box starts at the top with all of its text selected.
            Shown += delegate
            {
                entriesBox.Select(entriesBox.TextLength, 0);
                entriesBox.ScrollToCaret();
            };
        }

        void OnAdded(string line)
        {
            entriesBox.AppendText(entriesBox.TextLength == 0 ? line : "\r\n" + line);  // scrolls to it
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
