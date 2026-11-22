using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class ThemeChooser : ToolStripDropDown
{
    private readonly Panel board;
    private readonly Label heading;
    private readonly Label transparencyValue;
    private readonly ColorChoice[] choices;
    private readonly float scale;

    internal ThemeChooser(float scale, Action<PanelPalette> select, int transparency, Action<int> changeTransparency)
    {
        this.scale = scale;
        int step = (int)Math.Round(38 * scale);
        int inset = (int)Math.Round(12 * scale);
        Padding = new Padding(1);
        Renderer = new ToolStripProfessionalRenderer(new MenuColors());
        AccessibleName = "面板配色";
        board = new Panel { Size = new Size(inset * 2 + step * PanelPalette.Options.Length,
            (int)Math.Round(137 * scale)) };
        heading = new Label {
            AutoSize = false, Bounds = new Rectangle(inset + 2, inset - 2, board.Width - 2 * inset, (int)Math.Round(20 * scale)),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = ScreenTheme.Text, Font = ScreenTheme.UiFont(12, scale)
        };
        board.Controls.Add(heading);
        choices = new ColorChoice[PanelPalette.Options.Length];
        for (int index = 0; index < choices.Length; index++)
        {
            var palette = PanelPalette.Options[index];
            var choice = new ColorChoice(palette) {
                Bounds = new Rectangle(inset + step * index, (int)Math.Round(31 * scale),
                    (int)Math.Round(33 * scale), (int)Math.Round(31 * scale)),
            };
            choice.Click += (sender, e) =>
            {
                Close(ToolStripDropDownCloseReason.ItemClicked);
                select(palette);
            };
            choice.MouseEnter += (sender, e) => heading.Text = palette.Name;
            choice.MouseLeave += (sender, e) => heading.Text = ScreenTheme.Palette.Name;
            choices[index] = choice;
            board.Controls.Add(choice);
        }

        int divider = (int)Math.Round(70 * scale);
        board.Paint += (sender, e) =>
        {
            using (var line = new Pen(ScreenTheme.Hairline))
                e.Graphics.DrawLine(line, inset, divider, board.Width - inset, divider);
        };
        var caption = new Label {
            Text = "磨砂透明度", ForeColor = ScreenTheme.Text, Font = heading.Font,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(inset + 2, (int)Math.Round(79 * scale),
                (int)Math.Round(106 * scale), (int)Math.Round(19 * scale))
        };
        int valueWidth = (int)Math.Round(53 * scale);
        transparencyValue = new Label {
            Text = transparency + "%", ForeColor = ScreenTheme.Accent, Font = heading.Font,
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(board.Width - inset - valueWidth, caption.Top, valueWidth, caption.Height)
        };
        var slider = new TransparencySlider(scale) {
            Value = transparency,
            Bounds = new Rectangle(inset, (int)Math.Round(101 * scale), board.Width - 2 * inset,
                (int)Math.Round(25 * scale))
        };
        slider.ValueChanged += (sender, e) =>
        {
            transparencyValue.Text = slider.Value + "%";
            changeTransparency(slider.Value);
        };
        board.Controls.Add(caption);
        board.Controls.Add(transparencyValue);
        board.Controls.Add(slider);
        Items.Add(new ToolStripControlHost(board) { AutoSize = false, Size = board.Size, Margin = Padding.Empty });
    }

    internal void Open(Control trigger)
    {
        if (Visible)
        {
            Close();
            return;
        }
        heading.Text = ScreenTheme.Palette.Name;
        transparencyValue.ForeColor = ScreenTheme.Accent;
        board.BackColor = ScreenTheme.Field;
        BackColor = ScreenTheme.Field;
        foreach (var choice in choices)
            choice.Invalidate();
        Show(trigger, new Point(trigger.Width, trigger.Height + 4), ToolStripDropDownDirection.BelowLeft);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        using (var shape = ScreenTheme.Rounded(ClientRectangle, 12 * scale))
            Region = new Region(shape);
    }

    private sealed class ColorChoice : Button
    {
        private readonly PanelPalette palette;
        private bool hovered;

        internal ColorChoice(PanelPalette palette)
        {
            this.palette = palette;
            AccessibleName = palette.Name;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var backdrop = new SolidBrush(ScreenTheme.Field))
                graphics.FillRectangle(backdrop, ClientRectangle);
            bool active = ScreenTheme.Palette == palette;
            int size = Math.Min(Width, Height);
            int radius = (int)Math.Round(size * .38f);
            var circle = new RectangleF(Width / 2f - radius, Height / 2f - radius, radius * 2, radius * 2);
            using (var dark = new SolidBrush(PanelPalette.Mix(palette.Canvas, palette.Accent, .25f)))
            using (var light = new SolidBrush(palette.Accent))
            using (var border = new Pen(active || Focused || hovered ? ScreenTheme.Text : palette.Border, active ? 1.6f : 1f))
            {
                graphics.FillEllipse(dark, circle);
                float inset = radius * .33f;
                var center = RectangleF.Inflate(circle, -inset, -inset);
                graphics.FillEllipse(light, center);
                graphics.DrawEllipse(border, circle);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    }

    private sealed class TransparencySlider : TrackBar
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr unused, ref NativeRect rectangle);

        private readonly float scale;
        private int reportedValue;

        internal TransparencySlider(float scale)
        {
            this.scale = scale;
            AccessibleName = "磨砂透明度";
            Minimum = 0;
            Maximum = AppearancePreferences.MaximumTransparency;
            SmallChange = 1;
            LargeChange = 5;
            TickStyle = TickStyle.None;
            AutoSize = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        protected override void OnValueChanged(EventArgs e)
        {
            reportedValue = Value;
            Invalidate();
            base.OnValueChanged(e);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (IsHandleCreated && (message.Msg == 0x0201 || message.Msg == 0x0202 || message.Msg == 0x020a
                || message.Msg == 0x0100 || (Capture && (message.Msg == 0x0200 || message.Msg == 0x0113))))
            {
                // Native input can change the position without a reflected scroll event in the popup.
                if (Value != reportedValue)
                    OnValueChanged(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            using (var backdrop = new SolidBrush(ScreenTheme.Field))
                graphics.FillRectangle(backdrop, ClientRectangle);
            var thumb = new NativeRect();
            var channel = new NativeRect();
            SendMessage(Handle, 0x0419, IntPtr.Zero, ref thumb); // TBM_GETTHUMBRECT
            SendMessage(Handle, 0x041a, IntPtr.Zero, ref channel); // TBM_GETCHANNELRECT
            int width = channel.Right - channel.Left;
            if (width < 2)
                return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float center = (thumb.Left + thumb.Right) / 2f;
            float y = Height / 2f;
            var track = new RectangleF(channel.Left, y - 2 * scale, width, 4 * scale);
            var subdued = PanelPalette.Mix(ScreenTheme.Field, ScreenTheme.Text, .24f);
            ScreenTheme.Surface(graphics, track, 2 * scale, subdued, Color.Transparent);
            if (Value > Minimum && center > track.Left)
            {
                track.Width = Math.Min(track.Width, center - track.Left);
                ScreenTheme.Surface(graphics, track, 2 * scale, ScreenTheme.Accent, Color.Transparent);
            }
            float radius = 6.5f * scale;
            using (var fill = new SolidBrush(ScreenTheme.Text))
                graphics.FillEllipse(fill, center - radius, y - radius, 2 * radius, 2 * radius);
            if (Focused && ShowFocusCues)
                using (var ring = new Pen(ScreenTheme.Accent))
                    graphics.DrawEllipse(ring, center - radius - 1.5f, y - radius - 1.5f,
                        2 * radius + 3, 2 * radius + 3);
        }
    }
}
