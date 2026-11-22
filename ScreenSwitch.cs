using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        using (var instance = new Mutex(true, @"Local\ScreenSwitch.FloatingBall", out firstInstance))
        {
            if (!firstInstance)
                return;

            PhysicalDesktop.EnableNativeDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FloatingBall());
        }
    }
}

internal sealed class FloatingBall : Form
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSizeLimits
    {
        internal Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point position);

    private readonly System.Windows.Forms.Timer pointer = new System.Windows.Forms.Timer { Interval = 80 };
    private readonly System.Windows.Forms.Timer displayChange = new System.Windows.Forms.Timer { Interval = 250 };
    private readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false };
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly Icon trayIcon;
    private SettingsPanel settings;
    private readonly GlobalShortcut shortcut;
    private readonly float scale;
    private readonly string placementPath;
    private string anchorScreen, anchorDevicePath;
    private int rightOffset, bottomOffset;
    private DockEdge dockEdge;
    private Rectangle workArea, dockTab;
    private bool collapsed, waitForDockLeave;
    private Point grabPoint, startPosition;
    private bool tracking, dragging, hovered;
    private DateTime showAfter, hideAfter;

    internal FloatingBall(string shortcutPath = null, string placementPath = null)
    {
        shortcut = new GlobalShortcut(shortcutPath);
        this.placementPath = placementPath ?? BallPlacement.DefaultPath;
        Text = "屏幕切换";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = AppLogo.Background;
        Cursor = Cursors.Hand;
        AccessibleName = "显示设置";
        AccessibleRole = AccessibleRole.PushButton;
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            scale = graphics.DpiX / 96f;
        ClientSize = new Size(BallSize, BallSize);
        rightOffset = bottomOffset = Pixels(24);

        UpdateBallRegion();

        settings = CreateSettings(SettingsPanel.ReadableScale(scale, DesktopMonitors.Find(null).Work.Bounds));
        menu.ForeColor = ScreenTheme.Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
        var exitItem = new ToolStripMenuItem("退出", null, (sender, e) => Close());
        var hideItem = new ToolStripMenuItem("隐藏悬浮球", null, (sender, e) => ToggleVisibility());
        menu.Items.Add(new ToolStripMenuItem("打开显示设置", null, (sender, e) => OpenPanelFromMenu()));
        menu.Items.Add(new ToolStripMenuItem("快捷键设置", null, (sender, e) =>
        {
            OpenPanelFromMenu();
            settings.OpenShortcutSettings();
        }));
        menu.Items.Add(hideItem);
        menu.Items.Add(exitItem);
        menu.Opening += (sender, e) =>
        {
            hideItem.Text = Visible ? "隐藏悬浮球" : "显示悬浮球";
            hideItem.ShortcutKeyDisplayString = shortcut.Registered ? GlobalShortcut.Format(shortcut.Current) : "";
            hideItem.Enabled = (tray.Visible || shortcut.Registered) && !settings.HoldOpen;
            exitItem.Enabled = !settings.HoldOpen;
        };
        ContextMenuStrip = menu;
        trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        tray.Icon = trayIcon;
        tray.Text = Text;
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenPanelFromMenu();
        };

        pointer.Tick += WatchPointer;
        displayChange.Tick += (sender, e) =>
        {
            if (settings.DropDownOpen)
                return;
            displayChange.Stop();
            if (!tracking)
                PlaceBall();
            if (settings.Visible)
                settings.RefreshDisplay();
        };

        LoadPlacement();
        PlaceBall();
        waitForDockLeave = collapsed && dockTab.Contains(Cursor.Position);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        tray.Visible = true;
        pointer.Start();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (shortcut != null)
            shortcut.Attach(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (shortcut != null)
            shortcut.Detach();
        base.OnHandleDestroyed(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && shortcut != null && shortcut.Matches(message.WParam)) // WM_HOTKEY
        {
            if (settings == null || !settings.TryRecordRegisteredShortcut(shortcut.Current))
                ToggleVisibility();
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == 0x0024 && message.LParam != IntPtr.Zero) // WM_GETMINMAXINFO
        {
            // Keep Windows' normal minimum window width from stretching this small badge.
            var limits = (WindowSizeLimits)Marshal.PtrToStructure(message.LParam, typeof(WindowSizeLimits));
            limits.MinTrackSize = new Point(1, 1);
            Marshal.StructureToPtr(limits, message.LParam, false);
        }
        if (message.Msg == 0x007e || message.Msg == 0x001a) // WM_DISPLAYCHANGE or WM_SETTINGCHANGE
        {
            displayChange.Stop();
            displayChange.Start();
        }
    }

    private void ToggleVisibility()
    {
        if (Visible)
        {
            menu.Close();
            settings.Hide();
            tracking = dragging = hovered = false;
            Capture = false;
            showAfter = hideAfter = DateTime.MinValue;
            pointer.Stop();
            Hide();
        }
        else
        {
            PlaceBall();
            Show();
            pointer.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (collapsed)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
            var edge = PanelPalette.Mix(AppLogo.Background, ScreenTheme.Text, .36f);
            ScreenTheme.Surface(e.Graphics, bounds, Pixels(5), AppLogo.Background, edge);
            bool vertical = Height > Width;
            var marker = new Size(Pixels(vertical ? 2 : 16), Pixels(vertical ? 16 : 2));
            var center = new Rectangle((Width - marker.Width) / 2, (Height - marker.Height) / 2,
                marker.Width, marker.Height);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using (var ink = new SolidBrush(ScreenTheme.Accent))
                e.Graphics.FillRectangle(ink, center);
            return;
        }
        AppLogo.Draw(e.Graphics, ClientRectangle, hovered || settings.Visible);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        showAfter = DateTime.UtcNow.AddMilliseconds(180);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || settings.HoldOpen)
            return;
        if (collapsed)
            SetCollapsed(false);
        tracking = Capture = true;
        grabPoint = Cursor.Position;
        startPosition = Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!tracking)
            return;
        var delta = new Size(Cursor.Position.X - grabPoint.X, Cursor.Position.Y - grabPoint.Y);
        var threshold = SystemInformation.DragSize;
        if (!dragging && Math.Abs(delta.Width) < threshold.Width / 2
                      && Math.Abs(delta.Height) < threshold.Height / 2)
            return;
        dragging = true;
        settings.Hide();
        Location = Point.Add(startPosition, delta);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (tracking && !Capture)
            FinishDrag(Cursor.Position);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !tracking)
            return;
        bool click = !dragging;
        FinishDrag(Cursor.Position);
        Capture = false;
        if (click)
            OpenPanel();
    }

    private void FinishDrag(Point pointerPosition)
    {
        tracking = false;
        if (!dragging)
            return;
        dragging = false;
        var screen = DesktopMonitors.At(pointerPosition);
        var area = screen.Work.Bounds;
        if (screen.Name == null || area.IsEmpty)
            return;
        anchorScreen = screen.Name;
        try
        {
            var droppedOn = DisplayConfiguration.Read(false).Paths.Where(path => path.ScreenName == anchorScreen)
                .OrderByDescending(path => !string.IsNullOrEmpty(anchorDevicePath)
                    && string.Equals(path.DevicePath, anchorDevicePath, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            anchorDevicePath = (droppedOn.Flags & 1) != 0 ? droppedOn.DevicePath : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            anchorDevicePath = null;
        }
        rightOffset = Clamp(area.Right - Right, 0, area.Width - BallSize);
        bottomOffset = Clamp(area.Bottom - Bottom, 0, area.Height - BallSize);
        dockEdge = NearestEdge(Bounds, area);
        collapsed = dockEdge != DockEdge.None;
        showAfter = hideAfter = DateTime.MinValue;
        PlaceBall(area);
        waitForDockLeave = collapsed && dockTab.Contains(pointerPosition);
        new BallPlacement(anchorDevicePath, anchorScreen, rightOffset, bottomOffset, dockEdge).Save(placementPath);
    }

    private void WatchPointer(object sender, EventArgs e)
    {
        var cursor = Cursor.Position;
        bool overBall = (Bounds.Contains(cursor) && WindowFromPoint(cursor) == Handle)
            || (!collapsed && dockEdge != DockEdge.None && dockTab.Contains(cursor));
        UpdatePointer(overBall, settings.Bounds.Contains(cursor), DateTime.UtcNow);
    }

    private void UpdatePointer(bool overBall, bool overPanel, DateTime now)
    {
        if (tracking || menu.Visible)
            return;
        if (collapsed)
        {
            if (!overBall)
                waitForDockLeave = false;
            else if (!waitForDockLeave)
            {
                SetCollapsed(false);
                showAfter = now.AddMilliseconds(180);
            }
            return;
        }

        if (overBall || (settings.Visible && (overPanel || settings.HoldOpen)))
            hideAfter = DateTime.MinValue;
        else if (settings.Visible || dockEdge != DockEdge.None)
        {
            if (hideAfter == DateTime.MinValue)
                hideAfter = now.AddMilliseconds(450);
            else if (now >= hideAfter)
            {
                settings.Hide();
                if (dockEdge != DockEdge.None)
                    SetCollapsed(true);
                else
                    Invalidate();
                return;
            }
        }

        if (settings.Visible)
            return;
        if (!overBall)
            showAfter = DateTime.MinValue;
        else if (showAfter == DateTime.MinValue)
            showAfter = now.AddMilliseconds(180);
        else if (now >= showAfter)
            OpenPanel();
    }

    private void OpenPanel()
    {
        if (collapsed)
            SetCollapsed(false);
        if (!settings.Visible)
        {
            var area = DesktopMonitors.At(new Point(Left + Width / 2, Top + Height / 2)).Work.Bounds;
            var panelScale = SettingsPanel.ReadableScale(PhysicalDesktop.WindowScale(Handle, scale), area);
            if (settings.CanResizeBetweenShows && Math.Abs(settings.LayoutScale - panelScale) > .01f)
            {
                settings.Dispose();
                settings = CreateSettings(panelScale);
            }
            settings.RefreshDisplay();
            if (!area.IsEmpty)
                PlacePanel(area);
            settings.CaptureBackdrop();
            settings.Show(this);
        }
        hideAfter = DateTime.MinValue;
        Invalidate();
    }

    private SettingsPanel CreateSettings(float layoutScale)
    {
        var panel = new SettingsPanel(layoutScale, shortcut: shortcut);
        panel.CloseRequested += (sender, e) => panel.Hide();
        panel.DisplayChanged += (sender, e) => PlaceBall();
        return panel;
    }

    private void OpenPanelFromMenu()
    {
        if (!Visible)
            ToggleVisibility();
        OpenPanel();
        hideAfter = DateTime.UtcNow.AddSeconds(4);
    }

    private void LoadPlacement()
    {
        var saved = BallPlacement.Load(placementPath);
        if (saved != null)
        {
            anchorDevicePath = saved.DevicePath;
            anchorScreen = saved.ScreenName;
            rightOffset = saved.RightOffset;
            bottomOffset = saved.BottomOffset;
            dockEdge = saved.Edge;
            collapsed = dockEdge != DockEdge.None;
            return;
        }
        try
        {
            var primary = DesktopMonitors.Find(null).Name;
            var preferred = DisplayConfiguration.Read(false).Paths.OrderBy(path => path.BuiltIn)
                .ThenByDescending(path => path.ScreenName == primary).FirstOrDefault();
            if ((preferred.Flags & 1) != 0)
            {
                anchorDevicePath = preferred.DevicePath;
                anchorScreen = preferred.ScreenName;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            anchorScreen = null;
        }
    }

    private void PlaceBall()
    {
        string device = anchorScreen;
        if (!string.IsNullOrEmpty(anchorDevicePath))
        {
            try
            {
                var path = DisplayConfiguration.Read(false).Paths.FirstOrDefault(
                    item => string.Equals(item.DevicePath, anchorDevicePath, StringComparison.OrdinalIgnoreCase));
                device = (path.Flags & 1) != 0 ? path.ScreenName : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                device = null;
            }
        }
        var monitor = DesktopMonitors.Find(device);
        var area = monitor.Work.Bounds;
        if (area.IsEmpty)
            return;
        PlaceBall(area);
    }

    private void PlaceBall(Rectangle area)
    {
        workArea = area;
        int size = BallSize;
        var circle = new Rectangle(area.Right - size - Clamp(rightOffset, 0, area.Width - size),
            area.Bottom - size - Clamp(bottomOffset, 0, area.Height - size), size, size);
        int thickness = Pixels(10), length = Pixels(36);
        switch (dockEdge)
        {
            case DockEdge.Left:
                circle.X = area.Left;
                dockTab = new Rectangle(area.Left, circle.Y + (size - length) / 2, thickness, length);
                break;
            case DockEdge.Right:
                circle.X = area.Right - size;
                dockTab = new Rectangle(area.Right - thickness, circle.Y + (size - length) / 2, thickness, length);
                break;
            case DockEdge.Top:
                circle.Y = area.Top;
                dockTab = new Rectangle(circle.X + (size - length) / 2, area.Top, length, thickness);
                break;
            case DockEdge.Bottom:
                circle.Y = area.Bottom - size;
                dockTab = new Rectangle(circle.X + (size - length) / 2, area.Bottom - thickness, length, thickness);
                break;
            default:
                dockTab = Rectangle.Empty;
                break;
        }
        var target = collapsed ? dockTab : circle;
        bool resize = Size != target.Size;
        Bounds = target;
        if (resize)
            UpdateBallRegion();
        Invalidate();
        if (settings.Visible)
            PlacePanel(area);
    }

    private DockEdge NearestEdge(Rectangle ball, Rectangle area)
    {
        int left = Math.Max(0, ball.Left - area.Left), right = Math.Max(0, area.Right - ball.Right);
        int top = Math.Max(0, ball.Top - area.Top), bottom = Math.Max(0, area.Bottom - ball.Bottom);
        int distance = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (distance > Pixels(14))
            return DockEdge.None;
        if (distance == left) return DockEdge.Left;
        if (distance == right) return DockEdge.Right;
        return distance == top ? DockEdge.Top : DockEdge.Bottom;
    }

    private void SetCollapsed(bool value)
    {
        collapsed = value;
        waitForDockLeave = false;
        hovered = false;
        showAfter = hideAfter = DateTime.MinValue;
        if (!workArea.IsEmpty)
            PlaceBall(workArea);
    }

    private void UpdateBallRegion()
    {
        using (var shape = collapsed ? ScreenTheme.Rounded(ClientRectangle, Pixels(5)) : AppLogo.Outline(ClientRectangle))
            Region = new Region(shape);
        AccessibleName = collapsed ? "展开悬浮球" : "显示设置";
    }

    private void PlacePanel(Rectangle area)
    {
        int gap = Pixels(8);
        int y = Top - gap - settings.Height;
        if (y < area.Top && area.Bottom - Bottom > Top - area.Top)
            y = Bottom + gap;
        settings.Location = new Point(Clamp(Right - settings.Width, area.Left, area.Right - settings.Width),
            Clamp(y, area.Top, area.Bottom - settings.Height));
    }

    private int Pixels(int value) { return (int)Math.Round(value * scale); }
    private int BallSize { get { return Pixels(50); } }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && settings.HoldOpen)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pointer.Dispose();
            displayChange.Dispose();
            tray.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            settings.Dispose();
        }
        base.Dispose(disposing);
    }
}
