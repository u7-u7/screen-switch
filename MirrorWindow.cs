using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class MirrorWindow : Form
{
    private const uint SourceCopy = 0x00cc0020;
    private const uint CaptureLayered = 0x40000000;
    private const uint NoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010;
    private readonly string placementPath;
    private readonly Timer frames = new Timer { Interval = 33 };
    private readonly Timer screens = new Timer { Interval = 500 };
    private readonly PanelButton pin, minimize, expand, stop;
    private readonly ToolTip tooltips = new ToolTip { InitialDelay = 330, ReshowDelay = 160 };
    private readonly ImageAttributes frameSampling = new ImageAttributes();
    private readonly float scale;
    private Bitmap frame;
    private Graphics frameGraphics;
    private Rectangle sourceBounds;
    private Rectangle windowedBounds;
    private Size preferredWindowSize;
    private IntPtr panelHandle, overlayAbove;
    private DisplayConfiguration.Endpoint? windowMonitor;
    private bool fullscreen, changingBounds;
    private bool captureExclusionRequested, captureExcluded;
    private int failedFrames;

    internal string StopMessage { get; private set; }
    internal DisplayConfiguration.Endpoint SourceKey { get; private set; }

    internal MirrorWindow(DisplayConfiguration.Endpoint source, float scale, string placementPath)
    {
        SourceKey = source;
        this.scale = scale;
        this.placementPath = placementPath;
        Text = "屏幕投屏";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        BackColor = Color.FromArgb(12, 14, 17);
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(Pixels(168), Pixels(112));
        frameSampling.SetWrapMode(WrapMode.TileFlipXY);
        DoubleBuffered = true;
        ResizeRedraw = true;
        Font = new Font("Microsoft YaHei UI", 9f);
        pin = ToolbarButton("取消置顶", ScreenTheme.Symbol.Pin, (sender, e) => TogglePin());
        minimize = ToolbarButton("最小化", ScreenTheme.Symbol.Minimize, (sender, e) => WindowState = FormWindowState.Minimized);
        expand = ToolbarButton("全屏", ScreenTheme.Symbol.Fullscreen, (sender, e) => ToggleFullscreen());
        stop = ToolbarButton("停止投屏", ScreenTheme.Symbol.Close, (sender, e) => Close());
        LayoutTools();
        frames.Tick += (sender, e) => RenderFrame();
        screens.Tick += (sender, e) => CheckScreens();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        captureExclusionRequested = captureExcluded = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= 0x00040000; // WS_THICKFRAME enables native resizing.
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0083) // WM_NCCALCSIZE: the toolbar is drawn in the client.
        {
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == 0x0231) // WM_ENTERSIZEMOVE
        {
            changingBounds = true;
            frames.Stop();
        }
        if (message.Msg == 0x0214 && !fullscreen && !sourceBounds.IsEmpty) // WM_SIZING
        {
            ResizeAtEdge(message.WParam.ToInt32(), message.LParam);
            message.Result = new IntPtr(1);
            return;
        }
        if (message.Msg == 0x00a3 && message.WParam.ToInt32() == 2) // Caption double-click.
        {
            ToggleFullscreen();
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == 0x0232 && changingBounds && IsHandleCreated && !Disposing) // WM_EXITSIZEMOVE
        {
            changingBounds = false;
            BeginInvoke(new Action(() => RefreshScreens(true)));
        }
        if (message.Msg == 0x0084 && message.Result.ToInt32() == 1 && !fullscreen)
        {
            int packed = unchecked((int)message.LParam.ToInt64());
            var cursor = PointToClient(new Point((short)packed, (short)(packed >> 16)));
            message.Result = new IntPtr(WindowHit(cursor));
        }
        if (message.Msg == 0x007e && frames != null && frames.Enabled && IsHandleCreated && !Disposing)
        {
            frames.Stop();
            BeginInvoke(new Action(CheckScreens));
        }
    }

    internal void Start(IntPtr panel, IntPtr keepAbove)
    {
        panelHandle = panel;
        overlayAbove = keepAbove;
        using (PhysicalDesktop.Enter())
        {
            var paths = DisplayConfiguration.Read(false).Paths;
            sourceBounds = ReadSourceBounds(paths, SourceKey);
            PlaceInitialWindow(paths);
            if (!CaptureFrame())
                throw new InvalidOperationException("暂时无法读取来源屏画面。");
            Show();
            KeepBelowOverlay();
            UpdateToolbarVisibility(Cursor.Position);
            preferredWindowSize = Size;
            RememberPlacement(paths);
            frames.Start();
            screens.Start();
        }
    }

    internal void SwitchSource(DisplayConfiguration.Endpoint target)
    {
        if (target.Equals(SourceKey))
            return;
        using (PhysicalDesktop.Enter())
        {
            var bounds = ReadSourceBounds(DisplayConfiguration.Read(false).Paths, target);
            if (!CaptureNewFrame(bounds))
            {
                UpdateCaptureExclusion(sourceBounds);
                throw new InvalidOperationException("暂时无法读取这块屏的画面。");
            }
            SourceKey = target;
            sourceBounds = bounds;
            failedFrames = 0;
            if (!changingBounds && WindowState != FormWindowState.Minimized)
                ClampToDesktop();
            Invalidate();
        }
    }

    private void RenderFrame()
    {
        if (changingBounds || WindowState == FormWindowState.Minimized)
            return;
        using (PhysicalDesktop.Enter())
        {
            UpdateToolbarVisibility(Cursor.Position);
            if (!CaptureFrame())
            {
                if (++failedFrames >= 4)
                {
                    StopMessage = "来源屏画面暂时无法读取，投屏已停止。";
                    Close();
                }
                return;
            }
            failedFrames = 0;
            Invalidate();
        }
    }

    private void CheckScreens()
    {
        RefreshScreens(false);
    }

    private void RefreshScreens(bool rememberDrop)
    {
        if (IsDisposed || Disposing || !Visible || changingBounds)
            return;
        try
        {
            using (PhysicalDesktop.Enter())
            {
                var paths = DisplayConfiguration.Read(false).Paths;
                sourceBounds = ReadSourceBounds(paths, SourceKey);
                if (rememberDrop && !fullscreen && WindowState == FormWindowState.Normal)
                {
                    preferredWindowSize = Size;
                    RememberPlacement(paths);
                }
                RecoverWindowPosition(paths);
                frames.Start();
            }
        }
        catch (InvalidOperationException)
        {
            StopMessage = "显示配置已变化，投屏已停止。";
            Close();
        }
        catch (Win32Exception)
        {
            StopMessage = "暂时读不到显示配置，投屏已停止。";
            Close();
        }
    }

    private static Rectangle ReadSourceBounds(DisplayConfiguration.Path[] paths, DisplayConfiguration.Endpoint target)
    {
        var source = paths.FirstOrDefault(path => path.TargetKey.Equals(target));
        var screen = DesktopMonitors.Find(source.ScreenName, false);
        if (screen.Display.Bounds.IsEmpty)
            throw new InvalidOperationException("来源屏已关闭或拔出。");
        return screen.Display.Bounds;
    }

    private void PlaceInitialWindow(DisplayConfiguration.Path[] paths)
    {
        var saved = MirrorPlacement.Load(placementPath);
        if (saved != null)
        {
            var path = paths.FirstOrDefault(item => string.Equals(item.DevicePath, saved.DevicePath, StringComparison.OrdinalIgnoreCase));
            var work = WorkArea(DesktopMonitors.Find(path.ScreenName, false));
            if (!work.IsEmpty)
            {
                var bounds = saved.Bounds;
                bounds.Offset(work.Location);
                Bounds = FitWindow(bounds, work);
                return;
            }
        }
        Bounds = InitialWindow(PanelWorkArea());
    }

    private Rectangle PanelWorkArea()
    {
        var monitor = DesktopMonitors.FromWindow(IsWindowVisible(panelHandle) ? panelHandle : overlayAbove);
        return WorkArea(monitor);
    }

    private static Rectangle WorkArea(DesktopMonitors.Monitor monitor)
    {
        return monitor.Work.Bounds.IsEmpty ? monitor.Display.Bounds : monitor.Work.Bounds;
    }

    private void RememberPlacement(DisplayConfiguration.Path[] paths)
    {
        var monitor = DesktopMonitors.FromWindow(Handle);
        var path = paths.FirstOrDefault(item => item.ScreenName == monitor.Name);
        windowMonitor = (path.Flags & 1) != 0 ? (DisplayConfiguration.Endpoint?)path.TargetKey : null;
        if (!windowMonitor.HasValue)
            return;
        var devicePath = path.DevicePath;
        if (string.IsNullOrEmpty(devicePath))
            return;
        var work = WorkArea(monitor);
        var bounds = Bounds;
        bounds.Offset(-work.X, -work.Y);
        new MirrorPlacement(devicePath, bounds).Save(placementPath);
    }

    private void RecoverWindowPosition(DisplayConfiguration.Path[] paths)
    {
        if (WindowState == FormWindowState.Minimized)
            return;
        if (windowMonitor.HasValue && !paths.Any(path => path.TargetKey.Equals(windowMonitor.Value)))
        {
            var work = PanelWorkArea();
            if (!work.IsEmpty)
            {
                SetFullscreen(false, InitialWindow(work));
                preferredWindowSize = Size;
                RememberPlacement(paths);
            }
            return;
        }
        ClampToDesktop();
    }

    private void ClampToDesktop()
    {
        var current = DesktopMonitors.FromWindow(Handle);
        var display = current.Display.Bounds;
        if (display.IsEmpty)
            return;
        if (fullscreen)
        {
            var fitted = FitToDisplay(display);
            if (Bounds != fitted)
                Bounds = fitted;
        }
        else if (!MatchesSourceAspect(Size) || !DesktopMonitors.IntersectsDesktop(Bounds))
        {
            var desired = new Rectangle(Location, preferredWindowSize.IsEmpty ? Size : preferredWindowSize);
            Bounds = FitWindow(desired, WorkArea(current));
        }
    }

    private Rectangle InitialWindow(Rectangle work)
    {
        var source = sourceBounds.Size;
        var size = Fit(source, new Size(Math.Min(source.Width, work.Width), Math.Min(source.Height, work.Height))).Size;
        return ClampWindow(new Rectangle(work.Right - size.Width - Pixels(27), work.Top + Pixels(27), size.Width, size.Height), work);
    }

    private Rectangle FitToDisplay(Rectangle display)
    {
        var result = Fit(sourceBounds.Size, display.Size);
        result.Offset(display.Location);
        return result;
    }

    private bool MatchesSourceAspect(Size size)
    {
        var source = sourceBounds.Size;
        return Math.Abs((long)size.Width * source.Height - (long)size.Height * source.Width)
            <= Math.Max(source.Width, source.Height) / 2;
    }

    private Rectangle FitWindow(Rectangle desired, Rectangle work)
    {
        var source = sourceBounds.Size;
        var requested = desired.Size;
        if ((requested.Width > requested.Height) != (source.Width > source.Height))
            requested = new Size(requested.Height, requested.Width);
        double minimum = MinimumZoom();
        double maximum = FitZoom(source, work.Size);
        if (work.Contains(desired.Location))
        {
            var available = new Size(work.Right - desired.X, work.Bottom - desired.Y);
            double anchored = FitZoom(source, available);
            if (anchored >= minimum)
                maximum = anchored;
        }
        double zoom = Math.Max(minimum, FitZoom(source, requested));
        desired.Size = ScalePicture(source, Math.Min(zoom, maximum));
        return ClampWindow(desired, work);
    }

    private double MinimumZoom()
    {
        return Math.Max((double)MinimumSize.Width / sourceBounds.Width, (double)MinimumSize.Height / sourceBounds.Height);
    }

    private void ResizeAtEdge(int edge, IntPtr pointer)
    {
        var area = (DesktopMonitors.Rect)Marshal.PtrToStructure(pointer, typeof(DesktopMonitors.Rect));
        var desired = area.Bounds.Size;
        var source = sourceBounds.Size;
        double zoom;
        if (edge == 1 || edge == 2)
            zoom = (double)desired.Width / source.Width;
        else if (edge == 3 || edge == 6)
            zoom = (double)desired.Height / source.Height;
        else
        {
            // Project the requested corner onto the aspect-ratio diagonal.
            double width = source.Width, height = source.Height;
            zoom = (desired.Width * width + desired.Height * height) / (width * width + height * height);
            double longestSide = Math.Max(width, height);
            zoom = Math.Round(zoom * longestSide) / longestSide;
        }
        var size = ScalePicture(source, Math.Max(MinimumZoom(), zoom));
        if (edge == 1 || edge == 4 || edge == 7)
            area.Left = area.Right - size.Width;
        else
            area.Right = area.Left + size.Width;
        if (edge == 3 || edge == 4 || edge == 5)
            area.Top = area.Bottom - size.Height;
        else
            area.Bottom = area.Top + size.Height;
        Marshal.StructureToPtr(area, pointer, false);
    }

    internal static Rectangle ClampWindow(Rectangle desired, Rectangle work)
    {
        int width = Math.Min(desired.Width, work.Width);
        int height = Math.Min(desired.Height, work.Height);
        return new Rectangle(Math.Max(work.Left, Math.Min(desired.Left, work.Right - width)),
            Math.Max(work.Top, Math.Min(desired.Top, work.Bottom - height)), width, height);
    }

    private PanelButton ToolbarButton(string label, ScreenTheme.Symbol icon, EventHandler click)
    {
        var button = new PanelButton(scale, PanelButton.Appearance.Quiet, icon) {
            Size = new Size(Pixels(25), Pixels(25)),
            Visible = false
        };
        button.Click += click;
        Controls.Add(button);
        Describe(button, label);
        return button;
    }

    private void Describe(PanelButton button, string label)
    {
        button.AccessibleName = label;
        tooltips.SetToolTip(button, label);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (stop != null)
            LayoutTools();
    }

    private void LayoutTools()
    {
        int y = fullscreen ? Pixels(14) : Pixels(5);
        int right = ClientSize.Width - Pixels(9);
        foreach (var button in new[] { stop, expand, minimize, pin })
        {
            right -= button.Width;
            button.Location = new Point(right, y);
            right -= Pixels(4);
        }
        pin.Selected = TopMost;
    }

    private void TogglePin()
    {
        TopMost = !TopMost;
        Describe(pin, TopMost ? "取消置顶" : "置顶");
        pin.Selected = TopMost;
        KeepBelowOverlay();
    }

    private void KeepBelowOverlay()
    {
        if (TopMost && overlayAbove != IntPtr.Zero)
            SetWindowPos(Handle, overlayAbove, 0, 0, 0, 0, NoMoveSizeActivate);
    }

    private void ToggleFullscreen()
    {
        using (PhysicalDesktop.Enter())
        {
            var display = DesktopMonitors.FromWindow(Handle).Display.Bounds;
            if (display.IsEmpty)
                return;
            if (!fullscreen)
                windowedBounds = Bounds;
            SetFullscreen(!fullscreen, fullscreen ? windowedBounds : FitToDisplay(display));
            ClampToDesktop();
        }
    }

    private void SetFullscreen(bool value, Rectangle bounds)
    {
        fullscreen = value;
        expand.Icon = fullscreen ? ScreenTheme.Symbol.Restore : ScreenTheme.Symbol.Fullscreen;
        Describe(expand, fullscreen ? "还原小窗" : "全屏");
        Bounds = bounds;
        LayoutTools();
        UpdateToolbarVisibility(Cursor.Position);
        Invalidate();
    }

    private void UpdateToolbarVisibility(Point cursor)
    {
        var hovered = Bounds.Contains(cursor) ? WindowFromPoint(cursor) : IntPtr.Zero;
        bool visible = hovered != IntPtr.Zero && (hovered == Handle || IsChild(Handle, hovered));
        if (stop.Visible == visible)
            return;
        pin.Visible = minimize.Visible = expand.Visible = stop.Visible = visible;
        Invalidate();
    }

    private int WindowHit(Point cursor)
    {
        int band = Pixels(5);
        bool left = cursor.X < band, right = cursor.X >= ClientSize.Width - band;
        bool top = cursor.Y < band, bottom = cursor.Y >= ClientSize.Height - band;
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        if (left) return 10;
        if (right) return 11;
        return cursor.Y < Pixels(36) ? 2 : 1;
    }

    private int Pixels(int value) { return (int)Math.Round(value * scale); }

    private bool CaptureFrame()
    {
        return frame != null && frame.Size == sourceBounds.Size
            ? CapturePicture(sourceBounds, frameGraphics) : CaptureNewFrame(sourceBounds);
    }

    private bool CaptureNewFrame(Rectangle bounds)
    {
        var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        Graphics graphics = null;
        try
        {
            graphics = Graphics.FromImage(picture);
            if (!CapturePicture(bounds, graphics))
                return false;
            ReleaseFrame();
            frame = picture;
            frameGraphics = graphics;
            picture = null;
            graphics = null;
            return true;
        }
        finally
        {
            if (graphics != null)
                graphics.Dispose();
            if (picture != null)
                picture.Dispose();
        }
    }

    private Rectangle UpdateCaptureExclusion(Rectangle bounds)
    {
        var overlap = Visible ? Rectangle.Intersect(Bounds, bounds) : Rectangle.Empty;
        bool overSource = overlap.Width > 0 && overlap.Height > 0;
        if (overSource != captureExclusionRequested)
        {
            captureExclusionRequested = overSource;
            if (overSource)
            {
                captureExcluded = SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE
                if (captureExcluded)
                    DwmFlush();
            }
            else if (captureExcluded)
            {
                SetWindowDisplayAffinity(Handle, 0);
                captureExcluded = false;
            }
        }

        return overlap;
    }

    private bool CapturePicture(Rectangle bounds, Graphics graphics)
    {
        var overlap = UpdateCaptureExclusion(bounds);
        var desktop = GetDC(IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            return false;
        try
        {
            var capture = graphics.GetHdc();
            int saved = 0;
            try
            {
                if (overlap.Width > 0 && overlap.Height > 0 && !captureExcluded)
                {
                    // Leave the last clean pixels in the overlap if DWM cannot exclude the window.
                    saved = SaveDC(capture);
                    if (saved == 0 || ExcludeClipRect(capture, overlap.Left - bounds.Left, overlap.Top - bounds.Top,
                        overlap.Right - bounds.Left, overlap.Bottom - bounds.Top) == 0)
                        return true;
                }
                return BitBlt(capture, 0, 0, bounds.Width, bounds.Height, desktop, bounds.X, bounds.Y, SourceCopy | CaptureLayered)
                    || BitBlt(capture, 0, 0, bounds.Width, bounds.Height, desktop, bounds.X, bounds.Y, SourceCopy);
            }
            finally
            {
                if (saved != 0)
                    RestoreDC(capture, saved);
                graphics.ReleaseHdc(capture);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, desktop);
        }
    }

    internal static Rectangle Fit(Size source, Size target)
    {
        var size = ScalePicture(source, FitZoom(source, target));
        return new Rectangle((target.Width - size.Width) / 2, (target.Height - size.Height) / 2, size.Width, size.Height);
    }

    private static double FitZoom(Size source, Size target)
    {
        return Math.Min((double)target.Width / source.Width, (double)target.Height / source.Height);
    }

    private static Size ScalePicture(Size source, double zoom)
    {
        return new Size(Math.Max(1, (int)Math.Round(source.Width * zoom)), Math.Max(1, (int)Math.Round(source.Height * zoom)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (frame == null)
        {
            e.Graphics.Clear(BackColor);
            return;
        }
        if (ClientSize == frame.Size)
            e.Graphics.DrawImageUnscaled(frame, Point.Empty);
        else
        {
            e.Graphics.InterpolationMode = changingBounds ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(frame, ClientRectangle, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, frameSampling);
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (stop.Visible)
        {
            var tools = Rectangle.FromLTRB(pin.Left - Pixels(5), pin.Top - Pixels(5),
                stop.Right + Pixels(5), stop.Bottom + Pixels(5));
            ScreenTheme.Surface(e.Graphics, tools, Pixels(13), ScreenTheme.Canvas, ScreenTheme.Hairline);
        }
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData != Keys.Escape)
            return base.ProcessCmdKey(ref message, keyData);
        if (fullscreen)
            ToggleFullscreen();
        else
            Close();
        return true;
    }

    private void ReleaseFrame()
    {
        if (frameGraphics != null)
            frameGraphics.Dispose();
        if (frame != null)
            frame.Dispose();
        frameGraphics = null;
        frame = null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        frames.Stop();
        screens.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            frames.Dispose();
            screens.Dispose();
            tooltips.Dispose();
            frameSampling.Dispose();
            ReleaseFrame();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height,
        uint options);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("gdi32.dll")]
    private static extern int SaveDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool RestoreDC(IntPtr dc, int saved);

    [DllImport("gdi32.dll")]
    private static extern int ExcludeClipRect(IntPtr dc, int left, int top, int right, int bottom);
}
