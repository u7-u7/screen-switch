using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class ScreenTheme
{
    internal static PanelPalette Palette = PanelPalette.Options[0];
    internal static Color Canvas { get { return Palette.Canvas; } }
    internal static Color Field { get { return Palette.Field; } }
    internal static Color GlassField { get { return Color.FromArgb(184, Palette.Field); } }
    internal static Color TabTrack { get { return Color.FromArgb(108, Palette.Field); } }
    internal static Color Hover { get { return Palette.Hover; } }
    internal static Color Border { get { return Palette.Border; } }
    internal static Color Hairline { get { return PanelPalette.Mix(Canvas, Text, .13f); } }
    internal static readonly Color Text = Color.FromArgb(239, 245, 243);
    internal static readonly Color Muted = Color.FromArgb(185, 197, 200);
    internal static readonly Color Disabled = Color.FromArgb(133, 148, 153);
    internal static Color Accent { get { return Palette.Accent; } }
    internal static Color AccentSurface { get { return Palette.AccentSurface; } }
    internal static Color AccentInk { get { return Palette.AccentInk; } }
    internal static readonly Color Notice = Color.FromArgb(247, 206, 138);

    internal static Font UiFont(float pixels, float scale, FontStyle style = FontStyle.Regular)
    {
        return new Font("Microsoft YaHei UI", Math.Max(1, (float)Math.Round(pixels * scale)), style, GraphicsUnit.Pixel);
    }

    internal enum Symbol { None, Extend, Clone, Single, Chevron, Close, Launch, Palette, Check, Grid, Cast, Pin, Keyboard,
        Minimize, Fullscreen, Restore }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(2 * radius, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Surface(Graphics graphics, RectangleF bounds, float radius, Color background, Color edge)
    {
        using (var shape = Rounded(bounds, radius))
        using (var fill = new SolidBrush(background))
        using (var stroke = new Pen(edge))
        {
            graphics.FillPath(fill, shape);
            if (edge.A != 0)
                graphics.DrawPath(stroke, shape);
        }
    }

    internal static void Glyph(Graphics graphics, Symbol symbol, Rectangle bounds, Color color)
    {
        var old = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 16f, bounds.Height / 16f);
        using (var pen = new Pen(color, 1.35f))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            switch (symbol)
            {
                case Symbol.Extend:
                    graphics.DrawRectangle(pen, 1, 3, 6, 8);
                    graphics.DrawRectangle(pen, 9, 3, 6, 8);
                    graphics.DrawLine(pen, 5, 14, 11, 14);
                    break;
                case Symbol.Clone:
                    graphics.DrawLines(pen, new[] { new Point(2, 10), new Point(2, 2), new Point(12, 2) });
                    graphics.DrawRectangle(pen, 5, 5, 9, 8);
                    break;
                case Symbol.Single:
                    graphics.DrawRectangle(pen, 2, 3, 12, 8);
                    graphics.DrawLine(pen, 8, 11, 8, 14);
                    graphics.DrawLine(pen, 5, 14, 11, 14);
                    break;
                case Symbol.Chevron:
                    graphics.DrawLines(pen, new[] { new Point(4, 6), new Point(8, 10), new Point(12, 6) });
                    break;
                case Symbol.Close:
                    graphics.DrawLine(pen, 4, 4, 12, 12);
                    graphics.DrawLine(pen, 12, 4, 4, 12);
                    break;
                case Symbol.Minimize:
                    graphics.DrawLine(pen, 4, 10.5f, 12, 10.5f);
                    break;
                case Symbol.Fullscreen:
                    graphics.DrawLines(pen, new[] { new Point(3, 7), new Point(3, 3), new Point(7, 3) });
                    graphics.DrawLines(pen, new[] { new Point(9, 3), new Point(13, 3), new Point(13, 7) });
                    graphics.DrawLines(pen, new[] { new Point(13, 9), new Point(13, 13), new Point(9, 13) });
                    graphics.DrawLines(pen, new[] { new Point(7, 13), new Point(3, 13), new Point(3, 9) });
                    break;
                case Symbol.Restore:
                    graphics.DrawRectangle(pen, 3, 6, 7, 7);
                    graphics.DrawLines(pen, new[] { new Point(6, 4), new Point(6, 3), new Point(13, 3),
                        new Point(13, 10), new Point(12, 10) });
                    break;
                case Symbol.Launch:
                    graphics.DrawLine(pen, 4, 12, 12, 4);
                    graphics.DrawLines(pen, new[] { new Point(5, 4), new Point(12, 4), new Point(12, 11) });
                    break;
                case Symbol.Palette:
                    using (var tint = new SolidBrush(color))
                    {
                        graphics.DrawEllipse(pen, 1.5f, 1.5f, 13, 13);
                        graphics.FillEllipse(tint, 5, 4.5f, 2.2f, 2.2f);
                        graphics.FillEllipse(tint, 9, 4.5f, 2.2f, 2.2f);
                        graphics.FillEllipse(tint, 7, 9, 2.2f, 2.2f);
                    }
                    break;
                case Symbol.Check:
                    graphics.DrawLines(pen, new[] { new PointF(3, 8), new PointF(6.5f, 11.5f), new PointF(13, 5) });
                    break;
                case Symbol.Grid:
                    graphics.DrawRectangle(pen, 2, 2, 4.5f, 4.5f);
                    graphics.DrawRectangle(pen, 9.5f, 2, 4.5f, 4.5f);
                    graphics.DrawRectangle(pen, 2, 9.5f, 4.5f, 4.5f);
                    graphics.DrawRectangle(pen, 9.5f, 9.5f, 4.5f, 4.5f);
                    break;
                case Symbol.Cast:
                    graphics.DrawLines(pen, new[] { new Point(2, 6), new Point(2, 3),
                        new Point(14, 3), new Point(14, 12), new Point(9, 12) });
                    graphics.DrawArc(pen, -2, 8, 10, 10, 270, 85);
                    graphics.DrawArc(pen, 0, 11, 5, 5, 270, 80);
                    break;
                case Symbol.Pin:
                    graphics.DrawLines(pen, new[] { new Point(5, 3), new Point(11, 3), new Point(10, 4),
                        new Point(10, 7), new Point(12, 9), new Point(4, 9), new Point(6, 7),
                        new Point(6, 4), new Point(5, 3) });
                    graphics.DrawLine(pen, 8, 9, 8, 14);
                    break;
                case Symbol.Keyboard:
                    using (var border = Rounded(new RectangleF(1.5f, 4, 13, 8), 1.65f))
                        graphics.DrawPath(pen, border);
                    graphics.DrawLine(pen, 4, 6.5f, 4.3f, 6.5f);
                    graphics.DrawLine(pen, 7.8f, 6.5f, 8.2f, 6.5f);
                    graphics.DrawLine(pen, 11.7f, 6.5f, 12, 6.5f);
                    graphics.DrawLine(pen, 5, 9.2f, 11, 9.2f);
                    break;
            }
        }
        graphics.Restore(old);
    }
}

internal sealed class MenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return ScreenTheme.Canvas; } }
    public override Color MenuBorder { get { return ScreenTheme.Border; } }
    public override Color MenuItemSelected { get { return ScreenTheme.Hover; } }
    public override Color MenuItemBorder { get { return ScreenTheme.Border; } }
}

internal sealed class PanelButton : Button
{
    internal enum Appearance { Segment, Navigation, Primary, Secondary, Quiet }
    private readonly Appearance appearance;
    private ScreenTheme.Symbol symbol;
    private readonly float scale;
    private bool hovered, pressed, selected;

    internal PanelButton(float scale, Appearance appearance, ScreenTheme.Symbol symbol)
    {
        this.scale = scale;
        this.appearance = appearance;
        this.symbol = symbol;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.Opaque, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    internal bool Selected
    {
        get { return selected; }
        set { selected = value; Invalidate(); }
    }

    internal ScreenTheme.Symbol Icon
    {
        get { return symbol; }
        set { symbol = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        SettingsPanel.PaintBehindControl(this, graphics, appearance);
        bool primary = appearance == Appearance.Primary && Enabled;
        bool subdued = !Enabled && !selected;
        bool hot = Enabled && (hovered || pressed);
        bool currentTab = appearance == Appearance.Navigation && selected;
        Color fill = primary ? (hot ? PanelPalette.Mix(ScreenTheme.Accent, Color.White, .24f) : ScreenTheme.Accent)
            : currentTab ? PanelPalette.Mix(ScreenTheme.Hover, ScreenTheme.Accent, .17f)
            : selected ? ScreenTheme.AccentSurface
            : hot ? ScreenTheme.Hover
            : appearance == Appearance.Primary || appearance == Appearance.Secondary ? ScreenTheme.GlassField
            : Color.Transparent;
        Color ink = primary ? ScreenTheme.AccentInk : selected ? ScreenTheme.Accent : subdued ? ScreenTheme.Disabled
            : appearance == Appearance.Quiet ? ScreenTheme.Muted : ScreenTheme.Text;
        if (Enabled && fill.A == 0 && (appearance == Appearance.Quiet || appearance == Appearance.Navigation))
            ink = SettingsPanel.ReadableInk(this, ink, appearance == Appearance.Navigation ? ScreenTheme.TabTrack : Color.Transparent);
        Color edge = Focused && ShowFocusCues && Enabled ? ScreenTheme.Accent
            : appearance == Appearance.Secondary || currentTab ? ScreenTheme.Hairline : Color.Transparent;
        float radius = appearance == Appearance.Quiet ? Height / 2f : 12 * scale;
        ScreenTheme.Surface(graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), radius, fill, edge);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        var label = Text;
        int glyphSize = symbol == ScreenTheme.Symbol.None ? 0 : (int)Math.Round(16 * scale);
        int separation = glyphSize != 0 && label.Length != 0 ? (int)Math.Round(7 * scale) : 0;
        int width = label.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, label, Font, Size.Empty, flags).Width;
        int left = (Width - width - glyphSize - separation) / 2;
        if (symbol != ScreenTheme.Symbol.None)
            ScreenTheme.Glyph(graphics, symbol, new Rectangle(left, (Height - glyphSize) / 2, glyphSize, glyphSize), ink);
        if (width != 0)
            TextRenderer.DrawText(graphics, label, Font, new Rectangle(left + glyphSize + separation, 0, width + 2, Height), ink, flags);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
}

internal sealed class DisplayCheckBox : CheckBox
{
    private readonly float scale;
    private bool hovered;

    internal DisplayCheckBox(float scale)
    {
        this.scale = scale;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.Opaque, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        SettingsPanel.PaintBehindControl(this, graphics);
        ScreenTheme.Surface(graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), 12 * scale,
            hovered ? ScreenTheme.Hover : Checked ? ScreenTheme.AccentSurface : ScreenTheme.GlassField,
            Focused ? ScreenTheme.Accent : Color.Transparent);
        int size = (int)Math.Round(17 * scale);
        var box = new Rectangle((int)Math.Round(11 * scale), (Height - size) / 2, size, size);
        ScreenTheme.Surface(graphics, box, 5 * scale, Checked ? ScreenTheme.Accent : ScreenTheme.Canvas,
            Checked ? ScreenTheme.Accent : ScreenTheme.Muted);
        if (Checked)
            ScreenTheme.Glyph(graphics, ScreenTheme.Symbol.Check, box, ScreenTheme.AccentInk);
        int textStart = box.Right + (int)Math.Round(11 * scale);
        TextRenderer.DrawText(graphics, Text, Font, new Rectangle(textStart, 0, Width - textStart - 8, Height),
            ScreenTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
}
