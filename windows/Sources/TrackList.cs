// TrackList.cs — the track table (File · Format · Loudness · Gain) and, while empty, the big drop zone.
// Drawn by hand so it looks the same at every monitor scale and can be saved as a picture (--snapshot).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Ohmega
{
    sealed class TrackList : Control
    {
        readonly Library lib;
        readonly VScrollBar bar = new VScrollBar { Visible = false };
        readonly ToolTip tip = new ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 20000 };
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        int hoverRow = -1, anchor = -1, focusRow = -1;
        string tipText;
        bool dropHighlight;
        Bitmap bigIcon;

        public TrackList(Library library)
        {
            lib = library;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Ui.Window;
            TabStop = true;
            Controls.Add(bar);
            bar.Scroll += (s, e) => Invalidate();
            bar.ValueChanged += (s, e) => Invalidate();
            menu.Items.Add("Show in File Explorer", null, (s, e) => ShowInExplorer());
            menu.Items.Add("Remove from List", null, (s, e) => lib.RemoveSelected());
        }

        public bool DropHighlight
        {
            get => dropHighlight;
            set { if (dropHighlight != value) { dropHighlight = value; Invalidate(); } }
        }

        float S => DeviceDpi / 96f;
        int HeaderH => (int)(30 * S);
        int RowH => (int)(34 * S);
        int Pad => (int)(14 * S);
        int ViewH => Math.Max(0, Height - HeaderH);
        int ScrollY => bar.Visible ? bar.Value : 0;

        /// Column x positions: file | format | loudness | gain | (scrollbar)
        (int file, int format, int loudness, int gain, int end) Columns()
        {
            int end = Width - (bar.Visible ? bar.Width : 0);
            int gain = end - (int)(170 * S);
            int loudness = gain - (int)(250 * S);
            int format = loudness - (int)(72 * S);
            if (format < (int)(200 * S))           // narrow window: the file column keeps a minimum
            {
                int shrink = (int)(200 * S) - format;
                format += shrink; loudness += shrink; gain += shrink;
            }
            return (Pad, format, loudness, gain, end);
        }

        /// Called by the window after every change of the library.
        public void Sync()
        {
            int content = lib.Tracks.Count * RowH;
            bool need = content > ViewH && lib.Tracks.Count > 0;
            bar.SetBounds(Width - SystemInformation.VerticalScrollBarWidth, HeaderH, SystemInformation.VerticalScrollBarWidth, ViewH);
            bar.Visible = need;
            if (need)
            {
                bar.Minimum = 0;
                bar.LargeChange = Math.Max(1, ViewH);
                bar.SmallChange = RowH;
                bar.Maximum = content - 1;
                bar.Value = Math.Min(bar.Value, Math.Max(0, content - ViewH));
            }
            if (focusRow >= lib.Tracks.Count) focusRow = lib.Tracks.Count - 1;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Sync(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Sync(); }

        // MARK: drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (lib.Tracks.Count == 0)
            {
                PaintEmpty(g);
                return;
            }
            var c = Columns();
            // header
            using (var head = new Font(Font, FontStyle.Regular))
            {
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, "File", head, new Rectangle(c.file, 0, c.format - c.file, HeaderH), Ui.Secondary, flags);
                TextRenderer.DrawText(g, "Format", head, new Rectangle(c.format, 0, c.loudness - c.format, HeaderH), Ui.Secondary, flags);
                TextRenderer.DrawText(g, "Loudness", head, new Rectangle(c.loudness, 0, c.gain - c.loudness, HeaderH), Ui.Secondary, flags);
                TextRenderer.DrawText(g, "Gain", head, new Rectangle(c.gain, 0, c.end - c.gain, HeaderH), Ui.Secondary, flags);
            }
            using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, 0, HeaderH - 1, Width, HeaderH - 1);

            g.SetClip(new Rectangle(0, HeaderH, c.end, ViewH));
            int first = Math.Max(0, ScrollY / RowH), last = Math.Min(lib.Tracks.Count - 1, (ScrollY + ViewH) / RowH);
            for (int i = first; i <= last; i++)
                PaintRow(g, lib.Tracks[i], i, new Rectangle(0, HeaderH + i * RowH - ScrollY, c.end, RowH), c);
            g.ResetClip();

            if (DropHighlight) PaintDropFrame(g, new RectangleF(6 * S, 6 * S, Width - 12 * S, Height - 12 * S));
        }

        void PaintRow(Graphics g, Track t, int index, Rectangle r, (int file, int format, int loudness, int gain, int end) c)
        {
            if (t.Selected || index == hoverRow)
                using (var brush = new SolidBrush(t.Selected ? Ui.Selected : Ui.Hover)) g.FillRectangle(brush, r);
            if (index == focusRow && Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -1, -1));
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            // File: name + album pill
            int fileRight = c.format - (int)(12 * S);
            string album = t.AlbumName;
            int pillW = 0;
            if (album != null)
            {
                using (var small = new Font(Font.FontFamily, Font.SizeInPoints - 0.75f))
                {
                    int textW = Math.Min(TextRenderer.MeasureText(album, small, Size.Empty, TextFormatFlags.NoPadding).Width, (int)(150 * S));
                    pillW = textW + (int)(14 * S);
                    int nameW = Math.Min(TextRenderer.MeasureText(t.Name, Font, Size.Empty, TextFormatFlags.NoPadding).Width,
                                         fileRight - c.file - pillW - (int)(8 * S));
                    nameW = Math.Max(0, nameW);
                    TextRenderer.DrawText(g, t.Name, Font, new Rectangle(c.file, r.Y, nameW, r.Height), Ui.Text, flags | TextFormatFlags.PathEllipsis);
                    var pill = new RectangleF(c.file + nameW + 8 * S, r.Y + (r.Height - 18 * S) / 2, pillW, 18 * S);
                    Ui.Fill(g, Color.FromArgb(251, 227, 213), pill, 9 * S);
                    TextRenderer.DrawText(g, album, small, Rectangle.Round(pill), Ui.OrangePressed,
                        flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            else
            {
                TextRenderer.DrawText(g, t.Name, Font, new Rectangle(c.file, r.Y, fileRight - c.file, r.Height), Ui.Text, flags | TextFormatFlags.PathEllipsis);
            }

            // Format: lossless grey, lossy orange
            using (var mono = new Font("Consolas", Font.SizeInPoints))
                TextRenderer.DrawText(g, t.Format, mono, new Rectangle(c.format, r.Y, c.loudness - c.format, r.Height),
                    t.IsLossless ? Ui.Secondary : Ui.Warn, flags);

            // Loudness: value + meter from −30 to −4 LUFS with a tick at the target
            var cell = new Rectangle(c.loudness, r.Y, c.gain - c.loudness - (int)(16 * S), r.Height);
            if (t.ScanError != null)
                PaintGlyphText(g, Ui.GlyphWarning, "can't read", Ui.Red, cell);
            else if (t.Lufs is double lufs)
            {
                double off = lufs - lib.Target;
                Color color = Math.Abs(off) < 1 ? Ui.Green : off > 6 ? Ui.Red : Ui.Warn;
                int valueW = (int)(80 * S);
                TextRenderer.DrawText(g, Engine.LufsText(lufs), Font, new Rectangle(cell.X, r.Y, valueW, r.Height), color,
                    flags | TextFormatFlags.Right);
                float mx = cell.X + valueW + 12 * S, mw = Math.Max(10, cell.Right - mx), my = r.Y + r.Height / 2f - 3 * S;
                Func<double, float> pos = v => (float)((Math.Min(Math.Max(v, -30), -4) + 30) / 26) * mw;
                Ui.Fill(g, Color.FromArgb(230, 230, 230), new RectangleF(mx, my, mw, 6 * S), 3 * S);
                Ui.Fill(g, Color.FromArgb(217, color), new RectangleF(mx, my, Math.Max(pos(lufs), 6 * S), 6 * S), 3 * S);
                using (var tick = new SolidBrush(Color.FromArgb(180, 31, 31, 31)))
                    g.FillRectangle(tick, mx + pos(lib.Target) - S, r.Y + r.Height / 2f - 6 * S, 2 * S, 12 * S);
            }
            else
                TextRenderer.DrawText(g, "—", Font, cell, Ui.Tertiary, flags);

            // Gain: what normalizing did
            var gainCell = new Rectangle(c.gain, r.Y, c.end - c.gain - (int)(8 * S), r.Height);
            var o = t.Outcome;
            if (o == null) TextRenderer.DrawText(g, "—", Font, gainCell, Ui.Tertiary, flags);
            else if (o.Kind == OutcomeKind.Applied)
                PaintGlyphText(g, Ui.GlyphCheck, Engine.GainText(o.Gain) + (t.Album != null ? " album" : ""), Ui.Green, gainCell);
            else if (o.Kind == OutcomeKind.Skipped) PaintGlyphText(g, Ui.GlyphCheck, "already right", Ui.Secondary, gainCell);
            else PaintGlyphText(g, Ui.GlyphCancel, "failed", Ui.Red, gainCell);
        }

        void PaintGlyphText(Graphics g, string glyph, string text, Color color, Rectangle cell)
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int x = cell.X;
            if (Ui.IconFont != null)
            {
                using (var font = Ui.Glyphs(9f))
                    TextRenderer.DrawText(g, glyph, font, new Rectangle(x, cell.Y, (int)(14 * S), cell.Height), color, flags);
                x += (int)(20 * S);
            }
            TextRenderer.DrawText(g, text, Font, new Rectangle(x, cell.Y, cell.Right - x, cell.Height), color, flags | TextFormatFlags.EndEllipsis);
        }

        void PaintEmpty(Graphics g)
        {
            var frame = new RectangleF(18 * S, 18 * S, Width - 36 * S, Height - 36 * S);
            if (DropHighlight) PaintDropFrame(g, frame);
            else
                using (var path = Ui.Rounded(frame, 14 * S))
                using (var pen = new Pen(Color.FromArgb(200, 200, 200), 2 * S) { DashPattern = new[] { 5f, 3.5f } })
                    g.DrawPath(pen, path);

            int iconSize = (int)(96 * S);
            using (var title = new Font(Font.FontFamily, 18f, FontStyle.Regular))
            using (var mono = new Font("Consolas", 9.5f))
            {
                string sub = "Files are normalized one by one. A folder becomes an album — one shared gain,\nso its quiet intro stays quiet and the drop stays big.";
                string formats = "FLAC · WAV · AIFF · WavPack · MP3 · AAC · M4A · OGG · OPUS · WMA";
                var titleSize = TextRenderer.MeasureText("Drop your music here", title);
                var subSize = TextRenderer.MeasureText(sub, Font, Size.Empty, TextFormatFlags.HorizontalCenter);
                int total = iconSize + (int)(18 * S) + titleSize.Height + (int)(10 * S) + subSize.Height + (int)(14 * S) + mono.Height;
                int y = Math.Max((int)(30 * S), (Height - total) / 2);
                if (bigIcon == null) using (var icon = Ui.AppIcon(256)) bigIcon = icon.ToBitmap();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bigIcon, new Rectangle((Width - iconSize) / 2, y, iconSize, iconSize));
                y += iconSize + (int)(18 * S);
                TextRenderer.DrawText(g, "Drop your music here", title, new Rectangle(0, y, Width, titleSize.Height), Ui.Text, TextFormatFlags.HorizontalCenter);
                y += titleSize.Height + (int)(10 * S);
                TextRenderer.DrawText(g, sub, Font, new Rectangle(0, y, Width, subSize.Height), Ui.Secondary, TextFormatFlags.HorizontalCenter);
                y += subSize.Height + (int)(14 * S);
                TextRenderer.DrawText(g, formats, mono, new Rectangle(0, y, Width, mono.Height + 4), Ui.Tertiary, TextFormatFlags.HorizontalCenter);
            }
        }

        void PaintDropFrame(Graphics g, RectangleF frame)
        {
            Ui.Fill(g, Color.FromArgb(18, Ui.Orange), frame, 12 * S);
            using (var path = Ui.Rounded(frame, 12 * S))
            using (var pen = new Pen(Ui.Orange, 3 * S) { DashPattern = new[] { 3f, 2f } })
                g.DrawPath(pen, path);
        }

        // MARK: mouse and keyboard

        int RowAt(Point p)
        {
            if (p.Y < HeaderH || lib.Tracks.Count == 0 || p.X >= Columns().end) return -1;
            int i = (p.Y - HeaderH + ScrollY) / RowH;
            return i < lib.Tracks.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != hoverRow) { hoverRow = row; Invalidate(); }
            string text = null;
            if (row >= 0)
            {
                var t = lib.Tracks[row];
                var c = Columns();
                if (e.X >= c.gain && t.Outcome?.Kind == OutcomeKind.Failed) text = t.Outcome.Message;
                else if (e.X >= c.loudness && e.X < c.gain && t.ScanError != null) text = t.ScanError;
                else if (e.X < c.format) text = t.Path + (t.Album != null ? "\nAlbum: one shared gain for this folder" : "");
            }
            if (text != tipText) { tipText = text; tip.SetToolTip(this, text ?? ""); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverRow != -1) { hoverRow = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int row = RowAt(e.Location);
            if (row < 0)
            {
                if (e.Button == MouseButtons.Left && e.Y >= HeaderH) SelectOnly(-1);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (!lib.Tracks[row].Selected) SelectOnly(row);
                menu.Items[1].Enabled = lib.CanEdit;
                menu.Show(this, e.Location);
                return;
            }
            if ((ModifierKeys & Keys.Shift) != 0 && anchor >= 0) SelectRange(anchor, row, (ModifierKeys & Keys.Control) != 0);
            else if ((ModifierKeys & Keys.Control) != 0)
            {
                lib.Tracks[row].Selected = !lib.Tracks[row].Selected;
                anchor = focusRow = row;
            }
            else SelectOnly(row);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (RowAt(e.Location) >= 0) ShowInExplorer();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!bar.Visible) return;
            int lines = SystemInformation.MouseWheelScrollLines > 0 ? SystemInformation.MouseWheelScrollLines : 3;
            ScrollTo(bar.Value - e.Delta * lines * RowH / 120);
        }

        void ScrollTo(int value)
        {
            if (!bar.Visible) return;
            bar.Value = Math.Max(0, Math.Min(value, bar.Maximum - bar.LargeChange + 1));
        }

        void SelectOnly(int row)
        {
            foreach (var t in lib.Tracks) t.Selected = false;
            if (row >= 0) lib.Tracks[row].Selected = true;
            anchor = focusRow = row;
            Invalidate();
        }

        void SelectRange(int from, int to, bool keep)
        {
            if (!keep) foreach (var t in lib.Tracks) t.Selected = false;
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) lib.Tracks[i].Selected = true;
            focusRow = to;
            Invalidate();
        }

        void Reveal(int row)
        {
            if (row < 0 || !bar.Visible) return;
            int top = row * RowH, bottom = top + RowH;
            if (top < bar.Value) ScrollTo(top);
            else if (bottom > bar.Value + ViewH) ScrollTo(bottom - ViewH);
        }

        protected override bool IsInputKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
            }
            return base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int n = lib.Tracks.Count;
            if (n == 0) return;
            int page = Math.Max(1, ViewH / RowH - 1), row = focusRow;
            switch (e.KeyCode)
            {
                case Keys.Up: row = Math.Max(0, row - 1); break;
                case Keys.Down: row = Math.Min(n - 1, row + 1); break;
                case Keys.Home: row = 0; break;
                case Keys.End: row = n - 1; break;
                case Keys.PageUp: row = Math.Max(0, row - page); break;
                case Keys.PageDown: row = Math.Min(n - 1, row + page); break;
                case Keys.A when e.Control:
                    foreach (var t in lib.Tracks) t.Selected = true;
                    Invalidate();
                    e.Handled = true;
                    return;
                case Keys.Delete when !e.Shift:
                    lib.RemoveSelected();
                    e.Handled = true;
                    return;
                case Keys.Apps:
                    if (lib.Tracks.Any(t => t.Selected))
                        menu.Show(this, new Point(Pad, HeaderH + Math.Max(0, focusRow) * RowH - ScrollY + RowH));
                    e.Handled = true;
                    return;
                default: return;
            }
            if (row < 0) row = 0;
            if (e.Shift && anchor >= 0) SelectRange(anchor, row, false);
            else SelectOnly(row);
            Reveal(row);
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        void ShowInExplorer()
        {
            var t = lib.Tracks.FirstOrDefault(x => x.Selected);
            if (t == null) return;
            try { Process.Start("explorer.exe", "/select," + Engine.Quote(t.Path)); }
            catch (Exception) { }
        }
    }
}
