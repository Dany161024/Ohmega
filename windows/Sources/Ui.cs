// Ui.cs — looks (Windows 10 colours and fonts, Ohmega orange) and the flat buttons of the toolbar and status bar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Ohmega
{
    static class Ui
    {
        public static readonly Color Orange = Color.FromArgb(232, 105, 42);
        public static readonly Color OrangeHover = Color.FromArgb(242, 122, 60);
        public static readonly Color OrangePressed = Color.FromArgb(207, 90, 31);
        public static readonly Color Window = Color.White;
        public static readonly Color Bar = Color.FromArgb(243, 243, 243);
        public static readonly Color Line = Color.FromArgb(229, 229, 229);
        public static readonly Color Text = Color.FromArgb(31, 31, 31);
        public static readonly Color Secondary = Color.FromArgb(102, 102, 102);
        public static readonly Color Tertiary = Color.FromArgb(160, 160, 160);
        public static readonly Color Hover = Color.FromArgb(229, 243, 255);         // Explorer list hover
        public static readonly Color Selected = Color.FromArgb(204, 232, 255);      // Explorer list selection
        public static readonly Color Green = Color.FromArgb(16, 124, 16);
        public static readonly Color Red = Color.FromArgb(196, 43, 28);
        public static readonly Color Warn = Color.FromArgb(202, 80, 16);

        /// Segoe MDL2 Assets — the icon font of Windows 10 (null where it is missing, e.g. Wine).
        public static readonly string IconFont =
            FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;

        public const string GlyphAdd = "", GlyphFolder = "", GlyphDelete = "", GlyphSettings = "",
            GlyphInfo = "", GlyphSearch = "", GlyphVolume = "", GlyphCheck = "",
            GlyphCancel = "", GlyphWarning = "", GlyphChevron = "";

        public static Font Glyphs(float points) => IconFont == null ? null : new Font(IconFont, points, GraphicsUnit.Point);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = Rounded(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static Icon AppIcon(int size = 0)
        {
            using (var stream = typeof(Ui).Assembly.GetManifestResourceStream("Ohmega.ico"))
                return stream == null ? SystemIcons.Application : size > 0 ? new Icon(stream, size, size) : new Icon(stream);
        }
    }

    /// A flat Windows 10 button: icon glyph + text. Primary = the orange Normalize button; Bordered = a normal push button.
    sealed class FlatButton : Control
    {
        public string Glyph;
        public bool Primary, Bordered, Chevron;
        bool hover, pressed;

        public FlatButton(string text, string glyph, bool primary = false, bool bordered = false)
        {
            Text = text;
            Glyph = glyph;
            Primary = primary;
            Bordered = bordered;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = true;
        }

        float S => DeviceDpi / 96f;

        public Size Measure()
        {
            int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int glyph = Ui.IconFont != null && Glyph != null ? (int)(16 * S) : 0;
            int gap = text > 0 && glyph > 0 ? (int)(8 * S) : 0;
            int pad = (int)((text > 0 ? 12 : 9) * S);
            int chevron = Chevron ? (int)(20 * S) : 0;
            return new Size(pad * 2 + glyph + gap + text + chevron, (int)(32 * S));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color fill, ink;
            if (Primary)
            {
                fill = !Enabled ? Color.FromArgb(204, 204, 204) : pressed ? Ui.OrangePressed : hover ? Ui.OrangeHover : Ui.Orange;
                ink = Enabled ? Color.White : Color.FromArgb(122, 122, 122);
            }
            else
            {
                fill = !Enabled ? Color.Transparent : pressed ? Color.FromArgb(204, 204, 204) : hover ? Color.FromArgb(229, 229, 229)
                     : Bordered ? Color.White : Color.Transparent;
                ink = Enabled ? Ui.Text : Ui.Tertiary;
            }
            if (fill.A > 0) Ui.Fill(g, fill, r, 2 * S);
            if (Bordered && !Primary)
                using (var path = Ui.Rounded(r, 2 * S))
                using (var pen = new Pen(Enabled ? Color.FromArgb(173, 173, 173) : Color.FromArgb(214, 214, 214)))
                    g.DrawPath(pen, path);

            var size = Measure();
            int x = Chevron ? (int)(10 * S) : (Width - size.Width) / 2 + (int)((string.IsNullOrEmpty(Text) ? 9 : 12) * S);
            if (Chevron)
                using (var font = Ui.Glyphs(7.5f) ?? new Font(Font.FontFamily, 7f))
                    TextRenderer.DrawText(g, Ui.IconFont != null ? Ui.GlyphChevron : "v", font,
                        new Rectangle(Width - (int)(26 * S), 0, (int)(18 * S), Height), Enabled ? Ui.Secondary : Ui.Tertiary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Ui.IconFont != null && Glyph != null)
            {
                using (var font = Ui.Glyphs(10.5f))
                {
                    var box = new Rectangle(x, 0, (int)(16 * S), Height);
                    TextRenderer.DrawText(g, Glyph, font, box, Primary || !Enabled ? ink : (Text.Length > 0 ? Ui.Text : Ui.Secondary),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                x += (int)(24 * S);
            }
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(3 * S), -(int)(3 * S)));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { hover = pressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }
}
