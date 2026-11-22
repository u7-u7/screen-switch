using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal sealed class PanelPicker : Control
{
    private static readonly TextFormatFlags TextStyle = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private readonly float scale;
    private readonly PickerDropDown popup;
    private readonly OptionList list;
    private readonly ToolStripControlHost host;
    private object[] items = new object[0];
    private int selectedIndex = -1;
    private bool hovered;
    private DateTime ignoreFieldClickUntil;

    internal event EventHandler SelectedIndexChanged;
    internal IReadOnlyList<object> Items { get { return items; } }
    internal bool DroppedDown { get { return popup.Visible; } }
    internal object SelectedItem { get { return selectedIndex < 0 ? null : items[selectedIndex]; } }

    internal PanelPicker(float scale)
    {
        this.scale = scale;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        TabStop = true;
        ImeMode = ImeMode.Disable;
        AccessibleRole = AccessibleRole.ComboBox;
        Cursor = Cursors.Hand;
        list = new OptionList(this, scale);
        popup = new PickerDropDown(this, scale);
        host = new ToolStripControlHost(list) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        popup.Items.Add(host);
        popup.Closed += (sender, e) =>
        {
            ignoreFieldClickUntil = e.CloseReason == ToolStripDropDownCloseReason.AppClicked
                && RectangleToScreen(ClientRectangle).Contains(Cursor.Position)
                ? DateTime.UtcNow.AddMilliseconds(180) : DateTime.MinValue;
            list.StopAnimation();
            Invalidate();
        };
    }

    internal void SetItems(IEnumerable<object> options, object selected)
    {
        var previous = SelectedItem;
        ClosePopup();
        items = options.ToArray();
        selectedIndex = selected == null ? -1 : Array.IndexOf(items, selected);
        NotifySelection(previous);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= items.Length)
            return;
        var previous = SelectedItem;
        ClosePopup();
        selectedIndex = index;
        NotifySelection(previous);
    }

    private void NotifySelection(object previous)
    {
        Invalidate();
        if (Equals(previous, SelectedItem))
            return;
        if (IsHandleCreated)
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        var changed = SelectedIndexChanged;
        if (changed != null)
            changed(this, EventArgs.Empty);
    }

    internal void ClosePopup()
    {
        if (popup.Visible)
            popup.Close();
    }

    private void OpenPopup()
    {
        if (popup.Visible || !Enabled || !Visible || items.Length == 0)
            return;
        var anchor = RectangleToScreen(ClientRectangle);
        var area = DesktopMonitors.At(new Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2)).Work.Bounds;
        int gap = Pixels(5);
        int desired = list.HeightForRows(Math.Min(6, items.Length)) + 2;
        int below = area.Bottom - anchor.Bottom - gap;
        int above = anchor.Top - area.Top - gap;
        bool openAbove = !area.IsEmpty && below < desired && above > below;
        int available = area.IsEmpty ? desired : openAbove ? above : below;
        int height = Math.Min(desired, Math.Max(list.HeightForRows(1) + 2, available));
        int width = area.IsEmpty ? anchor.Width : Math.Min(anchor.Width, area.Width);
        int left = area.IsEmpty ? anchor.Left : Math.Max(area.Left, Math.Min(anchor.Left, area.Right - width));
        int top = openAbove ? anchor.Top - gap - height : anchor.Bottom + gap;
        host.Size = new Size(Math.Max(1, width - 2), height - 2);
        list.Size = host.Size;
        list.Font = Font;
        list.Reset(selectedIndex);
        popup.BackColor = ScreenTheme.Field;
        popup.Opacity = .62;
        popup.Show(this, PointToClient(new Point(left, top)), ToolStripDropDownDirection.BelowRight);
        list.FadeIn();
        Invalidate();
    }

    private bool HandlePickerKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (!DroppedDown)
        {
            if (key != Keys.Space && key != Keys.Enter && key != Keys.F4 && key != Keys.Down && key != Keys.Up)
                return false;
            OpenPopup();
            return true;
        }

        switch (key)
        {
            case Keys.Escape:
            case Keys.F4:
                ClosePopup();
                return true;
            case Keys.Space:
            case Keys.Enter:
                Choose(list.ActiveIndex);
                return true;
            case Keys.Down:
                list.MoveActive(1);
                return true;
            case Keys.Up:
                list.MoveActive(-1);
                return true;
            case Keys.Home:
                list.ActivateItem(0);
                return true;
            case Keys.End:
                list.ActivateItem(items.Length - 1);
                return true;
            case Keys.PageDown:
                list.MoveActive(list.PageRows);
                return true;
            case Keys.PageUp:
                list.MoveActive(-list.PageRows);
                return true;
            default:
                return false;
        }
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        return HandlePickerKey(keyData) || base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
            Focus();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location))
            return;
        if (DateTime.UtcNow < ignoreFieldClickUntil)
        {
            ignoreFieldClickUntil = DateTime.MinValue;
            return;
        }
        if (DroppedDown)
            ClosePopup();
        else
            OpenPopup();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (DroppedDown)
            list.Wheel(e.Delta);
        else
            base.OnMouseWheel(e);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        ignoreFieldClickUntil = DateTime.MinValue;
        Invalidate();
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible && popup != null)
            ClosePopup();
    }
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled && popup != null)
            ClosePopup();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
            return;
        var graphics = e.Graphics;
        SettingsPanel.PaintBehindControl(this, graphics);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var edge = Enabled && (DroppedDown || (Focused && ShowFocusCues)) ? ScreenTheme.Accent
            : hovered && Enabled ? ScreenTheme.Hairline : Color.Transparent;
        ScreenTheme.Surface(graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), Pixels(12), ScreenTheme.GlassField, edge);
        int inset = Pixels(12);
        int icon = Pixels(16);
        TextRenderer.DrawText(graphics, SelectedItem == null ? "—" : SelectedItem.ToString(), Font,
            new Rectangle(inset, 0, Math.Max(0, Width - 2 * inset - icon - Pixels(4)), Height),
            Enabled ? ScreenTheme.Text : ScreenTheme.Disabled, TextStyle);
        var glyph = new Rectangle(Width - inset - icon, (Height - icon) / 2, icon, icon);
        if (DroppedDown)
        {
            var state = graphics.Save();
            graphics.RotateTransform(180);
            glyph.Offset(-2 * glyph.X - glyph.Width, -2 * glyph.Y - glyph.Height);
            ScreenTheme.Glyph(graphics, ScreenTheme.Symbol.Chevron, glyph, ScreenTheme.Accent);
            graphics.Restore(state);
        }
        else
            ScreenTheme.Glyph(graphics, ScreenTheme.Symbol.Chevron, glyph, Enabled ? ScreenTheme.Muted : ScreenTheme.Disabled);
    }

    private int Pixels(int value) { return (int)Math.Round(value * scale); }

    protected override AccessibleObject CreateAccessibilityInstance() { return new PickerAccessible(this); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && popup != null)
        {
            ClosePopup();
            popup.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class PickerAccessible : ControlAccessibleObject
    {
        private readonly PanelPicker picker;
        internal PickerAccessible(PanelPicker picker) : base(picker) { this.picker = picker; }
        public override string Value
        {
            get { return picker.SelectedItem == null ? "" : picker.SelectedItem.ToString(); }
        }
        public override string DefaultAction { get { return picker.DroppedDown ? "收起" : "展开"; } }
        public override AccessibleStates State
        {
            get { return base.State | (picker.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed); }
        }
        public override void DoDefaultAction()
        {
            if (picker.DroppedDown)
                picker.ClosePopup();
            else
                picker.OpenPopup();
        }
    }

    private sealed class PickerDropDown : ToolStripDropDown
    {
        private readonly PanelPicker picker;
        private readonly float radius;

        internal PickerDropDown(PanelPicker picker, float scale)
        {
            this.picker = picker;
            radius = 13 * scale;
            Padding = new Padding(1);
            Renderer = new PickerRenderer(radius);
            DropShadowEnabled = true;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            using (var shape = ScreenTheme.Rounded(ClientRectangle, radius))
                Region = new Region(shape);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            return picker.HandlePickerKey(keyData) || base.ProcessCmdKey(ref message, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            return picker.HandlePickerKey(keyData) || base.ProcessDialogKey(keyData);
        }
    }

    private sealed class PickerRenderer : ToolStripProfessionalRenderer
    {
        private readonly float radius;
        internal PickerRenderer(float radius) : base(new MenuColors()) { this.radius = radius; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var area = RectangleF.Inflate(e.ToolStrip.ClientRectangle, -.5f, -.5f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var stroke = new Pen(ScreenTheme.Hairline))
            using (var edge = ScreenTheme.Rounded(area, radius))
                e.Graphics.DrawPath(stroke, edge);
        }
    }

    private sealed class OptionList : Control
    {
        private readonly PanelPicker picker;
        private readonly int padding, rowHeight, gutter, thumbMin;
        private readonly Timer animation = new Timer { Interval = 15 };
        private readonly Stopwatch fade = new Stopwatch();
        private float offset, destination, dragStartOffset;
        private int highlighted = -1, pressed = -1, dragStartY;
        private Point mousePoint;
        private bool dragging, mouseInList;

        internal int ActiveIndex { get { return highlighted; } }
        internal int PageRows { get { return Math.Max(1, ViewHeight / rowHeight); } }
        private int ViewHeight { get { return Math.Max(1, Height - padding * 2); } }
        private int ScrollMax { get { return Math.Max(0, picker.items.Length * rowHeight - ViewHeight); } }

        internal OptionList(PanelPicker picker, float scale)
        {
            this.picker = picker;
            padding = (int)Math.Round(6 * scale);
            rowHeight = (int)Math.Round(32 * scale);
            gutter = (int)Math.Round(11 * scale);
            thumbMin = (int)Math.Round(22 * scale);
            ImeMode = ImeMode.Disable;
            AccessibleRole = AccessibleRole.List;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            animation.Tick += (sender, e) => Animate();
        }

        internal int HeightForRows(int count) { return padding * 2 + count * rowHeight; }

        internal void Reset(int selected)
        {
            StopAnimation();
            pressed = -1;
            dragging = mouseInList = false;
            highlighted = selected < 0 ? 0 : selected;
            offset = destination = 0;
            ScrollIntoView(highlighted);
            offset = destination;
        }

        internal void FadeIn()
        {
            fade.Restart();
            animation.Start();
        }

        internal void StopAnimation()
        {
            fade.Reset();
            animation.Stop();
        }

        internal void MoveActive(int delta) { ActivateItem(highlighted + delta); }

        internal void ActivateItem(int index)
        {
            mouseInList = false;
            highlighted = Math.Max(0, Math.Min(index, picker.items.Length - 1));
            ScrollIntoView(highlighted);
            animation.Start();
            Invalidate();
        }

        private void ScrollIntoView(int index)
        {
            int top = index * rowHeight;
            if (top < destination)
                SetTarget(top);
            else if (top + rowHeight > destination + ViewHeight)
                SetTarget(top + rowHeight - ViewHeight);
        }

        internal void Wheel(int delta)
        {
            SetTarget(destination - delta / 120f * rowHeight * 2.5f);
            animation.Start();
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); }

        private void SetTarget(float value) { destination = Math.Max(0, Math.Min(value, ScrollMax)); }

        private void Animate()
        {
            if (!picker.DroppedDown)
            {
                StopAnimation();
                return;
            }
            if (fade.IsRunning)
            {
                double amount = Math.Min(1, fade.Elapsed.TotalMilliseconds / 135);
                picker.popup.Opacity = .62 + .38 * (1 - Math.Pow(1 - amount, 2));
                if (amount >= 1)
                    fade.Reset();
            }
            float delta = destination - offset;
            if (Math.Abs(delta) > .3f)
                offset += delta * .34f;
            else
                offset = destination;
            if (mouseInList && !dragging)
                highlighted = HitRow(mousePoint);
            Invalidate();
            if (!fade.IsRunning && offset == destination)
                animation.Stop();
        }

        private RectangleF Thumb()
        {
            float height = Math.Min(ViewHeight, Math.Max(thumbMin, (float)ViewHeight * ViewHeight
                / Math.Max(1, picker.items.Length * rowHeight)));
            float top = ScrollMax == 0 ? padding : padding + (ViewHeight - height) * offset / ScrollMax;
            return new RectangleF(Width - gutter + 2, top, Math.Max(3, gutter / 2), height);
        }

        private bool IsTrack(Point point) { return ScrollMax > 0 && point.X >= Width - gutter; }

        private int HitRow(Point point)
        {
            if (!ClientRectangle.Contains(point) || point.Y < padding || point.Y >= Height - padding || IsTrack(point))
                return -1;
            int index = (int)((point.Y - padding + offset) / rowHeight);
            return index < picker.items.Length ? index : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            if (IsTrack(e.Location))
            {
                var thumb = Thumb();
                if (e.Y < thumb.Top || e.Y > thumb.Bottom)
                {
                    SetTarget(destination + (e.Y < thumb.Top ? -ViewHeight : ViewHeight));
                    animation.Start();
                }
                else
                {
                    dragging = true;
                    dragStartOffset = offset;
                    dragStartY = e.Y;
                    destination = offset;
                    Capture = true;
                }
                return;
            }
            pressed = HitRow(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouseInList = true;
            mousePoint = e.Location;
            if (dragging)
            {
                float travel = Math.Max(1, ViewHeight - Thumb().Height);
                SetTarget(dragStartOffset + (e.Y - dragStartY) * ScrollMax / travel);
                offset = destination;
            }
            else
                highlighted = HitRow(e.Location);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            mouseInList = false;
            highlighted = picker.selectedIndex;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            if (dragging)
            {
                dragging = false;
                Capture = false;
                return;
            }
            int index = HitRow(e.Location);
            if (index >= 0 && index == pressed)
                picker.Choose(index);
            pressed = -1;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
                dragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(ScreenTheme.Field);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var clip = graphics.Save();
            graphics.SetClip(new Rectangle(0, padding, Width, ViewHeight), CombineMode.Intersect);
            int xEnd = Width - (ScrollMax == 0 ? padding : gutter + 2);
            int first = (int)(offset / rowHeight);
            for (int index = first; index < picker.items.Length; index++)
            {
                int top = padding + index * rowHeight - (int)Math.Round(offset);
                if (top >= Height - padding)
                    break;
                bool selected = index == picker.selectedIndex;
                var row = new Rectangle(padding, top + 1, xEnd - padding, rowHeight - 2);
                if (selected || index == highlighted)
                    ScreenTheme.Surface(graphics, row, padding + 2, selected ? ScreenTheme.AccentSurface : ScreenTheme.Hover, Color.Transparent);
                int checkSize = padding * 2 + 3;
                var text = new Rectangle(row.Left + padding + 2, row.Top, Math.Max(0, row.Width - checkSize - 3 * padding), row.Height);
                TextRenderer.DrawText(graphics, picker.items[index].ToString(), Font, text,
                    selected ? ScreenTheme.Accent : ScreenTheme.Text, TextStyle | TextFormatFlags.PreserveGraphicsClipping);
                if (selected)
                    ScreenTheme.Glyph(graphics, ScreenTheme.Symbol.Check,
                        new Rectangle(row.Right - checkSize - padding, row.Top + (row.Height - checkSize) / 2,
                            checkSize, checkSize), ScreenTheme.Accent);
            }
            graphics.Restore(clip);
            if (ScrollMax > 0)
            {
                var thumb = Thumb();
                var track = new RectangleF(thumb.X, padding, thumb.Width, ViewHeight);
                ScreenTheme.Surface(graphics, track, thumb.Width / 2, ScreenTheme.Canvas, Color.Transparent);
                ScreenTheme.Surface(graphics, thumb, thumb.Width / 2, dragging ? ScreenTheme.Accent
                    : PanelPalette.Mix(ScreenTheme.Field, ScreenTheme.Muted, .46f), Color.Transparent);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ListAccessible(this); }

        private sealed class ListAccessible : ControlAccessibleObject
        {
            private readonly OptionList list;
            internal ListAccessible(OptionList list) : base(list) { this.list = list; }
            public override int GetChildCount() { return list.picker.items.Length; }
            public override AccessibleObject GetChild(int index)
            {
                return index >= 0 && index < GetChildCount() ? new ItemAccessible(list, index) : null;
            }
            public override AccessibleObject GetFocused() { return GetChild(list.highlighted); }
            public override AccessibleObject GetSelected() { return GetChild(list.picker.selectedIndex); }
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly OptionList list;
            private readonly int index;
            internal ItemAccessible(OptionList list, int index) { this.list = list; this.index = index; }
            public override string Name
            {
                get { return index < list.picker.items.Length ? list.picker.items[index].ToString() : ""; }
            }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return list.AccessibilityObject; } }
            public override Rectangle Bounds
            {
                get
                {
                    var view = new Rectangle(0, list.padding, list.Width, list.ViewHeight);
                    var option = new Rectangle(0, list.padding + index * list.rowHeight - (int)list.offset, list.Width, list.rowHeight);
                    var visible = Rectangle.Intersect(view, option);
                    return visible.Height > 0 ? list.RectangleToScreen(visible) : Rectangle.Empty;
                }
            }
            public override AccessibleStates State
            {
                get
                {
                    var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (index == list.picker.selectedIndex) state |= AccessibleStates.Selected;
                    if (index == list.highlighted) state |= AccessibleStates.Focused;
                    if (Bounds.IsEmpty) state |= AccessibleStates.Offscreen;
                    return state;
                }
            }
            public override string DefaultAction { get { return "选择"; } }
            public override void DoDefaultAction() { list.picker.Choose(index); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                animation.Dispose();
            base.Dispose(disposing);
        }
    }
}
