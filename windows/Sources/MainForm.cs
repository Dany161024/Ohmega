// MainForm.cs — the Ohmega window: toolbar (add, target, backups, profiles), track list, status bar (Scan, Normalize).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Ohmega
{
    sealed class MainForm : Form
    {
        public readonly Library Lib = new Library();
        public readonly TrackList List;
        readonly Panel toolbar = new Panel(), statusbar = new Panel();
        readonly FlatButton addFiles, addFolder, clear, profiles, about, scan, normalize;
        readonly Label targetLabel = new Label { Text = "Target", AutoSize = true, ForeColor = Ui.Secondary };
        readonly FlatButton target = new FlatButton("", null, bordered: true) { Chevron = true };
        readonly ContextMenuStrip targetMenu = new ContextMenuStrip();
        bool customShown = true;
        readonly NumericUpDown custom = new NumericUpDown { Minimum = -40, Maximum = -5, DecimalPlaces = 1, Increment = 0.5m, TextAlign = HorizontalAlignment.Right };
        readonly Label customUnit = new Label { Text = "LUFS", AutoSize = true, ForeColor = Ui.Secondary };
        readonly CheckBox backup = new CheckBox { Text = "Back up originals", AutoSize = true, FlatStyle = FlatStyle.System };
        readonly ProgressBar progress = new ProgressBar { Style = ProgressBarStyle.Continuous, Visible = false };
        readonly Label status = new Label { AutoEllipsis = true, ForeColor = Ui.Secondary, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label summary = new Label { AutoSize = true, ForeColor = Ui.Tertiary };
        readonly ContextMenuStrip profileMenu = new ContextMenuStrip();
        readonly ToolTip tips = new ToolTip();
        bool syncing;

        public MainForm()
        {
            Text = "Ohmega";
            Icon = Ui.AppIcon();
            Font = SystemFonts.MessageBoxFont;      // Segoe UI 9 on Windows 10
            BackColor = Ui.Window;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            AutoScaleMode = AutoScaleMode.None;     // everything is laid out in code from DeviceDpi

            List = new TrackList(Lib) { Dock = DockStyle.Fill, AllowDrop = true };
            addFiles = new FlatButton("Add Files", Ui.GlyphAdd);
            addFolder = new FlatButton("Add Folder", Ui.GlyphFolder);
            clear = new FlatButton("Clear", Ui.GlyphDelete);
            profiles = new FlatButton("Profiles", Ui.GlyphSettings);
            about = new FlatButton("", Ui.GlyphInfo);
            scan = new FlatButton("Scan", Ui.GlyphSearch, bordered: true);
            normalize = new FlatButton("Normalize", Ui.GlyphVolume, primary: true);

            for (int i = 0; i <= Engine.Targets.Length; i++)
            {
                int index = i;
                if (i == Engine.Targets.Length) targetMenu.Items.Add(new ToolStripSeparator());
                targetMenu.Items.Add(TargetText(i), null, (s, e) => { Lib.TargetIndex = index; SyncUi(); });
            }
            custom.Value = (decimal)Lib.CustomLufs;

            toolbar.Controls.AddRange(new Control[] { addFiles, addFolder, clear, targetLabel, target, custom, customUnit, backup, profiles, about });
            statusbar.Controls.AddRange(new Control[] { progress, status, summary, scan, normalize });
            Controls.Add(List);
            Controls.Add(toolbar);
            Controls.Add(statusbar);
            toolbar.Dock = DockStyle.Top;
            statusbar.Dock = DockStyle.Bottom;
            toolbar.BackColor = Ui.Window;
            statusbar.BackColor = Ui.Bar;
            toolbar.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1); };
            statusbar.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, statusbar.Width, 0); };
            toolbar.Layout += (s, e) => LayoutToolbar();
            statusbar.Layout += (s, e) => LayoutStatusbar();

            tips.SetToolTip(addFiles, "Add audio files — each is normalized on its own (Ctrl+O)");
            tips.SetToolTip(addFolder, "Add a folder — it becomes one album with a shared gain (Ctrl+Shift+O)");
            tips.SetToolTip(clear, "Remove all tracks from the list");
            tips.SetToolTip(target, "Loudness target");
            tips.SetToolTip(backup, $"Copy originals to “{Engine.BackupDirName}” next to them before changing anything");
            tips.SetToolTip(profiles, "Saved target and backup settings");
            tips.SetToolTip(about, "About Ohmega (F1)");
            tips.SetToolTip(scan, "Measure the loudness of every track (F5)");
            tips.SetToolTip(normalize, "Write the gain into the audio files (Ctrl+Enter)");

            addFiles.Click += (s, e) => AddFiles();
            addFolder.Click += (s, e) => AddFolders();
            clear.Click += (s, e) => Lib.Clear();
            about.Click += (s, e) => AboutBox.Show(this);
            scan.Click += async (s, e) => await Lib.Scan();
            normalize.Click += async (s, e) => await Lib.Normalize();
            profiles.Click += (s, e) => { BuildProfileMenu(); profileMenu.Show(profiles, new Point(0, profiles.Height)); };
            target.Click += (s, e) => targetMenu.Show(target, new Point(0, target.Height));
            custom.ValueChanged += (s, e) => { if (!syncing) { Lib.CustomLufs = (double)custom.Value; SyncUi(); } };
            backup.CheckedChanged += (s, e) => { if (!syncing) Lib.Backup = backup.Checked; };

            foreach (Control c in new Control[] { this, List, toolbar, statusbar })
            {
                c.AllowDrop = true;
                c.DragEnter += OnDragEnter;
                c.DragOver += OnDragEnter;
                c.DragLeave += (s, e) => List.DropHighlight = false;
                c.DragDrop += OnDragDrop;
            }

            Lib.Changed += SyncUi;
            Lib.Finished += message => Notifier.Done(this, message);
            DpiChanged += (s, e) => { ApplySizes(); PerformLayout(); };
            ApplySizes();
            Size = new Size((int)(980 * S), (int)(620 * S));
            SyncUi();
        }

        float S => DeviceDpi / 96f;

        static string TargetText(int i) => i < Engine.Targets.Length
            ? $"{Engine.Targets[i].Name}  {Engine.LufsText(Engine.Targets[i].Lufs).Replace(".0 ", " ")}" : "Custom";

        void ApplySizes()
        {
            toolbar.Height = (int)(52 * S);
            statusbar.Height = (int)(56 * S);
            MinimumSize = new Size((int)(860 * S), (int)(480 * S));
            foreach (var b in new[] { addFiles, addFolder, clear, profiles, about, scan, normalize }) b.Size = b.Measure();
            normalize.Width = Math.Max(normalize.Width, (int)(128 * S));
            scan.Width = Math.Max(scan.Width, (int)(96 * S));
            target.Height = (int)(30 * S);
            target.Width = (int)(215 * S);
            custom.Width = (int)(62 * S);
            progress.Size = new Size((int)(150 * S), (int)(6 * S));
        }

        void LayoutToolbar()
        {
            int h = toolbar.Height, gap = (int)(4 * S), x = (int)(10 * S);
            Func<Control, int> mid = c => (h - c.Height) / 2;
            foreach (var b in new[] { addFiles, addFolder, clear })
            {
                b.Location = new Point(x, mid(b));
                x += b.Width + gap;
            }
            int right = toolbar.Width - (int)(10 * S);
            foreach (var c in new Control[] { about, profiles })
            {
                right -= c.Width;
                c.Location = new Point(right, mid(c));
                right -= gap;
            }
            right -= (int)(10 * S);
            right -= backup.Width;
            backup.Location = new Point(right, mid(backup));
            right -= (int)(16 * S);
            if (customShown)
            {
                right -= customUnit.Width;
                customUnit.Location = new Point(right, mid(customUnit));
                right -= custom.Width + (int)(4 * S);
                custom.Location = new Point(right, mid(custom));
                right -= (int)(10 * S);
            }
            right -= target.Width;
            target.Location = new Point(right, mid(target));
            right -= targetLabel.Width + (int)(8 * S);
            targetLabel.Location = new Point(right, mid(targetLabel));
            targetLabel.Visible = right > x + (int)(8 * S);
        }

        void LayoutStatusbar()
        {
            int h = statusbar.Height, x = (int)(14 * S), right = statusbar.Width - (int)(12 * S);
            Func<Control, int> mid = c => (h - c.Height) / 2;
            right -= normalize.Width;
            normalize.Location = new Point(right, mid(normalize));
            right -= scan.Width + (int)(8 * S);
            scan.Location = new Point(right, mid(scan));
            right -= (int)(16 * S);
            if (summary.Visible)
            {
                right -= summary.Width;
                summary.Location = new Point(right, mid(summary));
                right -= (int)(16 * S);
            }
            if (progress.Visible)
            {
                progress.Location = new Point(x, mid(progress));
                x += progress.Width + (int)(12 * S);
            }
            status.SetBounds(x, 0, Math.Max(0, right - x), h);
        }

        /// The window follows the library after every change.
        void SyncUi()
        {
            syncing = true;
            bool idle = Lib.Work == Work.None;
            target.Text = TargetText(Lib.TargetIndex);
            for (int i = 0; i < targetMenu.Items.Count; i++)
                if (targetMenu.Items[i] is ToolStripMenuItem item)
                    item.Checked = item.Text == target.Text;
            bool isCustom = Lib.TargetIndex == Engine.Targets.Length;
            decimal value = (decimal)Math.Max(-40, Math.Min(-5, Lib.CustomLufs));
            if (custom.Value != value) custom.Value = value;
            if (customShown != isCustom) { customShown = custom.Visible = customUnit.Visible = isCustom; toolbar.PerformLayout(); }
            backup.Checked = Lib.Backup;
            syncing = false;

            addFiles.Enabled = addFolder.Enabled = idle;
            clear.Enabled = idle && Lib.Tracks.Count > 0;
            target.Enabled = custom.Enabled = backup.Enabled = profiles.Enabled = idle;
            scan.Enabled = Lib.CanScan;
            normalize.Enabled = Lib.CanNormalize;
            progress.Visible = !idle;
            if (!idle)
            {
                progress.Maximum = Math.Max(1, Lib.Total);
                progress.Value = Math.Min(progress.Maximum, Lib.Done);
            }
            status.Text = Lib.Status;
            summary.Text = Lib.Summary;
            summary.Visible = Lib.Tracks.Count > 0;
            statusbar.PerformLayout();
            List.Sync();
            Text = idle ? "Ohmega" : $"Ohmega — {(Lib.Work == Work.Scanning ? "scanning" : "normalizing")} {Lib.Done}/{Lib.Total}";
        }

        // MARK: adding music

        void AddFiles()
        {
            if (!Lib.CanEdit) return;
            var patterns = string.Join(";", Engine.Codecs.Keys.Select(k => "*." + k));
            using (var dialog = new OpenFileDialog
            {
                Title = "Add audio files — each is normalized on its own",
                Filter = $"Audio files|{patterns}|All files|*.*",
                Multiselect = true,
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) Lib.Add(dialog.FileNames);
            }
        }

        void AddFolders()
        {
            if (!Lib.CanEdit) return;
            var folders = FolderPicker.Pick(this, "Add folders — every folder becomes one album with a shared gain");
            if (folders.Count > 0) Lib.Add(folders);
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            bool ok = Lib.CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            List.DropHighlight = ok;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            List.DropHighlight = false;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                Array.Sort(paths, Engine.NaturalCompare);
                BeginInvoke((Action)(() => Lib.Add(paths)));     // let Explorer finish the drop first
            }
        }

        // MARK: profiles

        void BuildProfileMenu()
        {
            profileMenu.Items.Clear();
            if (Lib.Profiles.Count == 0)
                profileMenu.Items.Add(new ToolStripMenuItem("No saved profiles") { Enabled = false });
            else
            {
                foreach (var name in Lib.Profiles.Keys)
                {
                    string n = name;
                    profileMenu.Items.Add(n, null, (s, e) => Lib.ApplyProfile(n));
                }
                profileMenu.Items.Add(new ToolStripSeparator());
                var delete = new ToolStripMenuItem("Delete Profile");
                foreach (var name in Lib.Profiles.Keys)
                {
                    string n = name;
                    delete.DropDownItems.Add(n, null, (s, e) => Lib.DeleteProfile(n));
                }
                profileMenu.Items.Add(delete);
            }
            profileMenu.Items.Add(new ToolStripSeparator());
            profileMenu.Items.Add("Save Current Settings…", null, (s, e) => SaveProfile());
        }

        void SaveProfile()
        {
            string what = (Lib.TargetLabel == "Custom" ? Engine.LufsText(Lib.CustomLufs) : Lib.TargetLabel)
                + (Lib.Backup ? ", with backups" : ", without backups");
            var name = InputBox.Ask(this, "Save profile", what, "Profile name");
            if (!string.IsNullOrWhiteSpace(name)) Lib.SaveProfile(name.Trim());
        }

        // MARK: keyboard and closing

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.O && e.Control) { if (e.Shift) AddFolders(); else AddFiles(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5 || (e.KeyCode == Keys.R && e.Control)) { _ = Lib.Scan(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && e.Control) { _ = Lib.Normalize(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && e.Control && e.Shift) { Lib.Clear(); e.Handled = true; }
            else if (e.KeyCode == Keys.F1) { AboutBox.Show(this); e.Handled = true; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Lib.Work == Work.Normalizing && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(this, "Ohmega is still normalizing. Stop and quit? The files being worked on right now stay unchanged.",
                    "Ohmega", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
            }
            Engine.StopAll();
            base.OnFormClosing(e);
        }
    }
}
