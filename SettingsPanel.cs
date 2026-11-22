using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class SettingsPanel : Form
{
    private const int DesignWidth = 416, DesignHeight = 426;
    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRect, IntPtr updateRegion, uint flags);

    private sealed class MonitorChoice
    {
        internal DisplayConfiguration.Path Path;
        internal bool Active;
        internal int Number;
        internal string Title;
        public override string ToString() { return Title; }
    }

    private sealed class RefreshChoice
    {
        internal DisplayModes.Mode Mode;
        public override string ToString() { return Mode.Rate; }
    }

    private sealed class DirectionChoice
    {
        internal uint Value;
        public override string ToString()
        {
            return (Value % 2 == 0 ? "横屏 " : "竖屏 ") + (Value * 90) + "°";
        }
    }

    private sealed class GlassLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var ink = ReadableInk(this, ForeColor);
            if (ink == ForeColor)
            {
                base.OnPaint(e);
                return;
            }
            var alignment = TextAlign == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink, alignment | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private readonly float scale;
    private readonly Font fieldLabelFont;
    private readonly Label topology, screenStatus, notice;
    private readonly PanelButton extend, clone, single, close, paletteButton, apply, cancel, keep, systemSettings;
    private readonly PanelButton openCombination, applyCombination;
    private readonly PanelButton openArrangement, resetArrangement, applyArrangement;
    private readonly PanelButton openDisplay, openCast, castToggle;
    private readonly PanelButton openShortcuts;
    private readonly PanelPicker monitors, resolutions, rates, directions;
    private readonly PanelPicker castSource;
    private readonly ThemeChooser chooser;
    private readonly string palettePath;
    private readonly AppearancePreferences appearance;
    private readonly PanelFrame frame = new PanelFrame();
    private readonly FrostedSurface glass = new FrostedSurface();
    private readonly Panel displayTools, combination;
    private readonly Panel arrangementTools;
    private readonly DisplayArrangement arrangementMap;
    private readonly Panel castTools;
    private readonly ShortcutEditor shortcutTools;
    private readonly GlobalShortcut shortcut;
    private readonly FlowLayoutPanel combinationRows;
    private readonly Label combinationCount;
    private readonly Label arrangementStatus;
    private readonly Label castStatus;
    private readonly Timer confirmationTimer = new Timer { Interval = 250 };
    private readonly Timer appearanceTimer = new Timer { Interval = 320 };
    private readonly Timer glassPreviewTimer = new Timer { Interval = 16 };
    private readonly Stopwatch glassPreviewClock = new Stopwatch();
    private double displayedTransparency;
    private DisplayConfiguration.Layout active, previous;
    private DisplayConfiguration.Endpoint? selection;
    private DisplayModes.Mode currentMode;
    private DisplayModes.Mode[] available = new DisplayModes.Mode[0];
    private readonly Stopwatch confirmationClock = new Stopwatch();
    private bool updating, busy;
    private bool frameVisible;
    private MirrorWindow mirror;
    private string castMessage;

    internal event EventHandler CloseRequested;
    internal event EventHandler DisplayChanged;

    internal bool HoldOpen
    {
        get { return busy || previous != null || DropDownOpen || chooser.Visible || shortcutTools.Recording || arrangementMap.IsDragging; }
    }

    internal float LayoutScale { get { return scale; } }
    internal bool CanResizeBetweenShows
    {
        get { return mirror == null && !HoldOpen && !arrangementMap.HasChanges && !apply.Enabled; }
    }

    internal static float ReadableScale(float nativeScale, Rectangle work)
    {
        float preferred = nativeScale * 1.125f;
        if (work.IsEmpty)
            return preferred;
        return Math.Min(preferred, Math.Min(work.Width / (float)DesignWidth, work.Height / (float)DesignHeight));
    }

    internal bool DropDownOpen
    {
        get
        {
            return monitors.DroppedDown || resolutions.DroppedDown || rates.DroppedDown || directions.DroppedDown
                || castSource.DroppedDown;
        }
    }
    private MonitorChoice SelectedMonitor { get { return monitors.SelectedItem as MonitorChoice; } }

    internal SettingsPanel(float scale, string palettePath = null, GlobalShortcut shortcut = null)
    {
        this.scale = scale;
        this.shortcut = shortcut;
        this.palettePath = palettePath ?? AppearancePreferences.DefaultPath;
        appearance = AppearancePreferences.Load(this.palettePath);
        displayedTransparency = appearance.Transparency;
        ScreenTheme.Palette = appearance.Palette;
        Text = "显示设置";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Font = ScreenTheme.UiFont(14, scale);
        fieldLabelFont = new Font(Font, FontStyle.Bold);
        ForeColor = ScreenTheme.Text;
        BackColor = ScreenTheme.Canvas;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Pixels(DesignWidth), Pixels(DesignHeight));
        DoubleBuffered = true;
        var content = RectangleF.Inflate(ClientRectangle, -Pixels(2), -Pixels(2));
        using (var shape = ScreenTheme.Rounded(content, PanelFrame.CornerRadius(scale) - Pixels(2)))
            Region = new Region(shape);

        var title = AddLabel("显示设置", 73, 16, 125, 29);
        title.Font = ScreenTheme.UiFont(20, scale, FontStyle.Bold);
        chooser = new ThemeChooser(scale, ChangePalette, appearance.Transparency, ChangeTransparency);
        chooser.Closed += (sender, e) =>
        {
            if (glassPreviewTimer.Enabled)
                PresentGlassPreview(true);
            SaveAppearanceIfPending();
        };
        openShortcuts = AddButton("快捷键", 195, 16, 82, 28, PanelButton.Appearance.Quiet, ScreenTheme.Symbol.Keyboard,
            (sender, e) => OpenShortcutSettings());
        paletteButton = AddButton("配色", 281, 16, 72, 28, PanelButton.Appearance.Quiet, ScreenTheme.Symbol.Palette,
            (sender, e) => chooser.Open(paletteButton));
        paletteButton.AccessibleName = "切换配色";
        close = AddButton("", 362, 16, 32, 28, PanelButton.Appearance.Quiet, ScreenTheme.Symbol.Close,
            (sender, e) => CloseRequested(this, EventArgs.Empty));
        close.AccessibleName = "收起设置";
        topology = AddLabel("", 87, 45, 185, 18);
        topology.ForeColor = ScreenTheme.Muted;

        openDisplay = AddButton("显示", 26, 82, 83, 35, PanelButton.Appearance.Navigation, ScreenTheme.Symbol.Single,
            (sender, e) => ShowTools(null));
        openArrangement = AddButton("排列", 114, 82, 83, 35, PanelButton.Appearance.Navigation, ScreenTheme.Symbol.Extend,
            (sender, e) => OpenArrangement());
        openCombination = AddButton("屏幕组合", 202, 82, 108, 35, PanelButton.Appearance.Navigation, ScreenTheme.Symbol.Grid,
            (sender, e) => OpenCombination());
        openCast = AddButton("投屏", 315, 82, 75, 35, PanelButton.Appearance.Navigation, ScreenTheme.Symbol.Cast,
            (sender, e) => OpenCast());

        displayTools = new Panel { BackColor = Color.Transparent };
        Place(displayTools, 22, 138, 372, 267);
        AddFieldLabel("显示模式", 0, 0, 140, displayTools);
        displayTools.Paint += (sender, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PaintButtonTrack(e.Graphics, PanelButton.Appearance.Segment, displayTools.Location);
        };
        extend = AddButton("扩展", 5, 29, 117, 32, PanelButton.Appearance.Segment, ScreenTheme.Symbol.Extend,
            (sender, e) => ApplyTopology(DisplayConfiguration.Extend), displayTools);
        clone = AddButton("复制", 127, 29, 118, 32, PanelButton.Appearance.Segment, ScreenTheme.Symbol.Clone,
            (sender, e) => ApplyTopology(DisplayConfiguration.Clone), displayTools);
        single = AddButton("仅选中屏", 250, 29, 117, 32, PanelButton.Appearance.Segment, ScreenTheme.Symbol.Single,
            (sender, e) => ApplySingle(), displayTools);

        AddFieldLabel("选择屏幕", 0, 78, 92, displayTools);
        monitors = AddCombo("选择屏幕", 0, 101, 372, displayTools);
        screenStatus = AddLabel("", 99, 78, 273, 19, displayTools);
        screenStatus.TextAlign = ContentAlignment.MiddleRight;
        screenStatus.ForeColor = ScreenTheme.Muted;
        AddFieldLabel("分辨率", 0, 150, 134, displayTools);
        AddFieldLabel("刷新率", 144, 150, 102, displayTools);
        AddFieldLabel("屏幕方向", 256, 150, 116, displayTools);
        resolutions = AddCombo("分辨率", 0, 174, 134, displayTools);
        rates = AddCombo("刷新率", 144, 174, 102, displayTools);
        directions = AddCombo("屏幕方向", 256, 174, 116, displayTools);

        notice = AddLabel("", 0, 211, 372, 19, displayTools);
        notice.ForeColor = ScreenTheme.Muted;
        systemSettings = AddButton("系统显示设置", 0, 232, 128, 34, PanelButton.Appearance.Quiet, ScreenTheme.Symbol.Launch,
            (sender, e) => OpenWindowsSettings(), displayTools);
        apply = AddButton("应用", 262, 232, 110, 34, PanelButton.Appearance.Primary, ScreenTheme.Symbol.None,
            (sender, e) => ApplyMode(), displayTools);
        cancel = AddButton("恢复原设置", 140, 232, 110, 34, PanelButton.Appearance.Secondary, ScreenTheme.Symbol.None,
            async (sender, e) => await CompleteChange(false), displayTools);
        keep = AddButton("保留更改", 262, 232, 110, 34, PanelButton.Appearance.Primary, ScreenTheme.Symbol.None,
            async (sender, e) => await CompleteChange(true), displayTools);

        combination = new Panel { Visible = false, BackColor = Color.Transparent };
        Place(combination, 22, 138, 372, 267);
        AddLabel("勾选要开启的屏幕，多块屏之间使用扩展。", 0, 0, 372, 24, combination).ForeColor = ScreenTheme.Muted;
        combinationRows = new FlowLayoutPanel {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent
        };
        Place(combinationRows, 0, 39, 372, 173, combination);
        combinationCount = AddLabel("", 0, 232, 194, 34, combination);
        combinationCount.ForeColor = ScreenTheme.Muted;
        applyCombination = AddButton("应用组合", 262, 232, 110, 34, PanelButton.Appearance.Primary, ScreenTheme.Symbol.None,
            async (sender, e) => await ApplyCombination(), combination);

        arrangementTools = new Panel { Visible = false, BackColor = Color.Transparent };
        Place(arrangementTools, 22, 138, 372, 267);
        arrangementMap = new DisplayArrangement(scale);
        Place(arrangementMap, 0, 0, 372, 194, arrangementTools);
        arrangementMap.LayoutEdited += (sender, e) => UpdateArrangementActions();
        arrangementStatus = AddLabel("拖动屏幕，调整它们的相对位置。", 0, 199, 372, 24, arrangementTools);
        arrangementStatus.ForeColor = ScreenTheme.Muted;
        resetArrangement = AddButton("重置", 0, 232, 79, 34, PanelButton.Appearance.Quiet, ScreenTheme.Symbol.None,
            (sender, e) => arrangementMap.ResetLayout(), arrangementTools);
        applyArrangement = AddButton("应用排列", 262, 232, 110, 34, PanelButton.Appearance.Primary, ScreenTheme.Symbol.None,
            async (sender, e) => await ApplyArrangement(), arrangementTools);

        castTools = new Panel { Visible = false, BackColor = Color.Transparent };
        Place(castTools, 22, 138, 372, 267);
        AddFieldLabel("投屏选择", 0, 0, 372, castTools);
        castSource = AddCombo("投屏选择", 0, 26, 372, castTools);
        castStatus = AddLabel("", 0, 70, 372, 23, castTools);
        castStatus.ForeColor = ScreenTheme.Muted;
        castToggle = AddButton("开始投屏", 262, 232, 110, 34, PanelButton.Appearance.Primary, ScreenTheme.Symbol.None,
            (sender, e) => ToggleCast(), castTools);
        shortcutTools = new ShortcutEditor(scale, shortcut, fieldLabelFont);
        Place(shortcutTools, 22, 138, 372, 267);
        castSource.SelectedIndexChanged += (sender, e) => { if (!updating) ChangeCastSource(); };
        monitors.SelectedIndexChanged += (sender, e) => { if (!updating) UpdateMonitorModes(); };
        directions.SelectedIndexChanged += (sender, e) => { if (!updating) UpdateResolutions(ProposedMode()); };
        resolutions.SelectedIndexChanged += (sender, e) => { if (!updating) UpdateRates(ProposedMode().Hertz); };
        rates.SelectedIndexChanged += (sender, e) => { if (!updating) UpdateActions(); };
        confirmationTimer.Tick += ConfirmTick;
        appearanceTimer.Tick += (sender, e) => SaveAppearanceIfPending();
        glassPreviewTimer.Tick += (sender, e) => PresentGlassPreview();
        UpdateActions();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED batches the panel and its child controls.
            return parameters;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (glass.Image == null)
            {
                displayedTransparency = appearance.Transparency;
                glass.Tint(ClientSize, BackColor, displayedTransparency);
            }
            FollowFrame();
            if (mirror != null && !busy && previous == null)
                ShowTools(castTools);
            if (castTools.Visible)
                PopulateCastSources();
        }
        else
        {
            if (frame != null)
                frame.Hide();
            if (chooser != null && !chooser.IsDisposed && chooser.Visible)
                chooser.Close();
            frameVisible = false;
            if (glass != null)
                glass.Clear();
            if (shortcutTools != null)
                ShowTools(null);
        }
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        return (shortcutTools != null && shortcutTools.Visible && shortcutTools.Record(keyData))
            || base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (shortcutTools != null)
            shortcutTools.CancelRecording();
    }

    internal bool TryRecordRegisteredShortcut(Keys keys)
    {
        return Visible && ContainsFocus && shortcutTools.Visible && shortcutTools.Record(keys);
    }

    internal void OpenShortcutSettings()
    {
        if (shortcut == null || busy || previous != null)
            return;
        shortcutTools.Begin();
        ShowTools(shortcutTools);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        FollowFrame();
    }

    private void FollowFrame(bool materialChanged = false)
    {
        if (frame == null || IsDisposed || Disposing || !Visible || !IsHandleCreated)
            return;
        try
        {
            frameVisible = frame.Follow(this, scale, glass.Image, materialChanged);
        }
        catch (Win32Exception error)
        {
            frameVisible = false;
            frame.Hide();
            Debug.WriteLine(error);
        }
    }

    internal void CaptureBackdrop()
    {
        glass.Capture(Bounds, scale);
        displayedTransparency = appearance.Transparency;
        glass.Tint(ClientSize, BackColor, displayedTransparency);
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (glass == null || glass.Image == null)
            base.OnPaintBackground(e);
        else
            e.Graphics.DrawImageUnscaled(glass.Image, 0, 0);
    }

    internal static void PaintBehindControl(Control control, Graphics graphics,
        PanelButton.Appearance appearance = PanelButton.Appearance.Quiet)
    {
        Point offset;
        var panel = FindPanel(control, out offset);
        if (panel != null && panel.glass.Image != null)
            graphics.DrawImageUnscaled(panel.glass.Image, -offset.X, -offset.Y);
        else
        {
            using (var brush = new SolidBrush(ScreenTheme.Canvas))
                graphics.FillRectangle(brush, control.ClientRectangle);
        }
        if (panel != null && (appearance == PanelButton.Appearance.Navigation || appearance == PanelButton.Appearance.Segment))
            panel.PaintButtonTrack(graphics, appearance, offset);
    }

    internal static Color ReadableInk(Control control, Color ink, Color surface = default(Color))
    {
        Point offset;
        var panel = FindPanel(control, out offset);
        var background = panel == null ? null : panel.glass.Image;
        offset.Offset(control.Width / 2, control.Height / 2);
        if (background == null || offset.X < 0 || offset.Y < 0 || offset.X >= background.Width || offset.Y >= background.Height)
            return ink;
        var color = background.GetPixel(offset.X, offset.Y);
        if (surface.A > 0)
            color = PanelPalette.Mix(color, surface, surface.A / 255f);
        int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        return brightness >= 133 ? ScreenTheme.Canvas : ink;
    }

    private static SettingsPanel FindPanel(Control control, out Point offset)
    {
        offset = Point.Empty;
        while (control != null && !(control is SettingsPanel))
        {
            offset.Offset(control.Location);
            control = control.Parent;
        }
        return control as SettingsPanel;
    }

    private void PaintButtonTrack(Graphics graphics, PanelButton.Appearance appearance, Point offset)
    {
        bool navigation = appearance == PanelButton.Appearance.Navigation;
        var bounds = navigation ? ScaledRect(22, 78, 372, 43) : ScaledRect(0, 25, 372, 40);
        if (!navigation)
            bounds.Offset(displayTools.Location);
        bounds.Offset(-offset.X, -offset.Y);
        ScreenTheme.Surface(graphics, bounds, Pixels(navigation ? 15 : 13),
            navigation ? ScreenTheme.TabTrack : ScreenTheme.GlassField, Color.Transparent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!frameVisible)
        {
            var inset = RectangleF.Inflate(ClientRectangle, -Pixels(2) - .5f, -Pixels(2) - .5f);
            using (var border = new Pen(ScreenTheme.Hairline))
            using (var outline = ScreenTheme.Rounded(inset, PanelFrame.CornerRadius(scale) - Pixels(2)))
                graphics.DrawPath(border, outline);
        }
        AppLogo.Draw(graphics, ScaledRect(20, 18, 43, 43));
        using (var status = new SolidBrush(ScreenTheme.Accent))
            graphics.FillEllipse(status, ScaledRect(75, 51, 6, 6));
        PaintButtonTrack(graphics, PanelButton.Appearance.Navigation, Point.Empty);
    }

    private void ChangePalette(PanelPalette palette)
    {
        appearance.Palette = palette;
        ScreenTheme.Palette = palette;
        appearance.Save(palettePath);
        BackColor = ScreenTheme.Canvas;
        RefreshGlass();
        if (mirror != null)
            mirror.Invalidate(true);
    }

    private void ChangeTransparency(int transparency)
    {
        appearance.Transparency = transparency;
        if (!glassPreviewTimer.Enabled)
        {
            glassPreviewClock.Restart();
            glassPreviewTimer.Start();
        }
        appearanceTimer.Stop();
        appearanceTimer.Start();
    }

    private void PresentGlassPreview(bool finish = false)
    {
        if (!Visible || Disposing)
        {
            glassPreviewTimer.Stop();
            return;
        }
        double elapsed = glassPreviewClock.Elapsed.TotalMilliseconds;
        glassPreviewClock.Restart();
        double target = appearance.Transparency;
        double difference = target - displayedTransparency;
        if (finish || Math.Abs(difference) < .45)
        {
            displayedTransparency = target;
            glassPreviewTimer.Stop();
        }
        else
            displayedTransparency += difference * (1 - Math.Exp(-elapsed / 28d));
        RefreshGlass();
    }

    private void RefreshGlass()
    {
        glass.Tint(ClientSize, BackColor, displayedTransparency);
        FollowFrame(true);
        const uint invalidate = 0x0001, noErase = 0x0020, allChildren = 0x0080, updateNow = 0x0100;
        if (IsHandleCreated)
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, invalidate | noErase | allChildren | updateNow);
    }

    private void SaveAppearanceIfPending()
    {
        if (!appearanceTimer.Enabled)
            return;
        appearanceTimer.Stop();
        appearance.Save(palettePath);
    }

    internal void RefreshDisplay()
    {
        if (busy || DropDownOpen)
            return;
        try
        {
            active = DisplayConfiguration.Read(false);
            var activePaths = active.Paths.ToDictionary(path => path.TargetKey);
            var choices = DisplayConfiguration.Read(true).Paths
                .Where(path => path.Target.Available != 0)
                .Concat(active.Paths)
                .GroupBy(path => path.TargetKey)
                .Select(group =>
                {
                    DisplayConfiguration.Path path;
                    bool enabled = activePaths.TryGetValue(group.Key, out path);
                    if (!enabled)
                        path = group.First();
                    return new MonitorChoice { Path = path, Active = enabled };
                })
                .OrderByDescending(item => item.Path.BuiltIn)
                .ThenBy(item => item.Path.Target.Id)
                .ThenBy(item => item.Path.Target.Adapter.High)
                .ThenBy(item => item.Path.Target.Adapter.Low).ToArray();
            for (int index = 0; index < choices.Length; index++)
            {
                var item = choices[index];
                item.Number = index + 1;
                item.Title = item.Number + " · " + item.Path.FriendlyName + (item.Active ? "" : "（已关闭）");
            }
            var selected = choices.FirstOrDefault(item => selection.HasValue && item.Path.TargetKey.Equals(selection.Value))
                ?? choices.FirstOrDefault(item => item.Active && !item.Path.BuiltIn)
                ?? choices.FirstOrDefault(item => item.Active);

            updating = true;
            monitors.SetItems(choices, selected);
            topology.Text = "当前 · " + TopologyName()
                + (choices.Length > active.Paths.Length ? " · " + active.Paths.Length + "/" + choices.Length : "");
        }
        catch (Win32Exception error)
        {
            updating = true;
            active = null;
            monitors.SetItems(new object[0], null);
            topology.Text = "暂时读不到显示设置（" + error.NativeErrorCode + "）";
        }
        finally
        {
            updating = false;
        }
        UpdateMonitorModes(true);
        if (arrangementTools.Visible)
        {
            var numbers = monitors.Items.Cast<MonitorChoice>().ToDictionary(choice => choice.Path.TargetKey, choice => choice.Number);
            arrangementMap.SetLayout(DisplayConfiguration.ExtendedAreas(active), numbers);
            UpdateArrangementActions();
        }
        if (castTools.Visible)
            PopulateCastSources();
    }

    private string TopologyName()
    {
        if (active.Topology == DisplayConfiguration.Extend)
            return "扩展屏幕";
        if (active.Topology == DisplayConfiguration.Clone)
            return "复制屏幕";
        return active.Paths.Length == 1 ? "单屏显示" : "自定义布局";
    }

    private void UpdateMonitorModes(bool preservePending = false)
    {
        var lastMonitor = selection;
        var lastMode = currentMode;
        var staged = ProposedMode();
        var monitor = SelectedMonitor;
        var device = monitor != null && monitor.Active ? monitor.Path.ScreenName : null;
        selection = monitor == null ? (DisplayConfiguration.Endpoint?)null : monitor.Path.TargetKey;
        available = new DisplayModes.Mode[0];
        if (device != null)
        {
            try
            {
                currentMode = DisplayModes.ReadCurrent(device);
                available = DisplayModes.ReadAvailable(device, currentMode);
                var sharedSource = active.Paths.Count(path => path.SourceKey.Equals(monitor.Path.SourceKey)) > 1;
                screenStatus.Text = currentMode.Resolution + " · " + currentMode.Rate + " · " + (currentMode.Orientation * 90) + "°"
                    + (sharedSource ? " · 复制屏幕共用设置" : "");
            }
            catch (InvalidOperationException error)
            {
                screenStatus.Text = error.Message;
            }
        }
        else
        {
            screenStatus.Text = monitor == null ? "" : "屏幕已关闭，可用「仅选中屏」启用。";
        }

        updating = true;
        if (!preservePending || !lastMonitor.HasValue || !selection.Equals(lastMonitor)
            || !DisplayModes.SameMode(lastMode, currentMode))
            staged = currentMode;
        var choices = available.Select(mode => mode.Orientation).Distinct().OrderBy(value => value)
            .Select(value => new DirectionChoice { Value = value }).ToArray();
        var direction = choices.FirstOrDefault(option => option.Value == staged.Orientation)
            ?? choices.FirstOrDefault(option => option.Value == currentMode.Orientation);
        directions.SetItems(choices, direction);
        updating = false;
        UpdateResolutions(staged);
    }

    private DisplayModes.Mode ProposedMode()
    {
        var rate = rates.SelectedItem as RefreshChoice;
        return rate == null ? currentMode : rate.Mode;
    }

    private void UpdateResolutions(DisplayModes.Mode preferred)
    {
        var direction = directions.SelectedItem as DirectionChoice;
        var modes = available.Where(mode => direction != null && direction.Value == mode.Orientation).ToArray();
        var choices = modes.Select(mode => mode.Resolution).Distinct().ToArray();
        bool swap = direction != null && preferred.Orientation % 2 != direction.Value % 2;
        uint width = swap ? preferred.Height : preferred.Width;
        uint height = swap ? preferred.Width : preferred.Height;
        var resolution = modes.OrderBy(mode => Math.Abs((long)mode.Width - width) + Math.Abs((long)mode.Height - height))
            .Select(mode => mode.Resolution).FirstOrDefault();
        updating = true;
        resolutions.SetItems(choices, resolution);
        updating = false;
        UpdateRates(preferred.Hertz);
    }

    private void UpdateRates(uint preferred)
    {
        var direction = directions.SelectedItem as DirectionChoice;
        var options = available.Where(mode => direction != null && direction.Value == mode.Orientation
            && mode.Resolution == (string)resolutions.SelectedItem)
            .GroupBy(mode => mode.Hertz).Select(group => new RefreshChoice { Mode = group.First() }).ToArray();
        updating = true;
        var choice = options.OrderBy(option => Math.Abs((long)option.Mode.Hertz - preferred)).FirstOrDefault();
        rates.SetItems(options, choice);
        updating = false;
        UpdateActions();
    }

    private void UpdateActions()
    {
        bool confirming = previous != null;
        bool ready = !busy && !confirming && active != null;
        var monitor = SelectedMonitor;
        uint arrangement = active == null ? 0 : active.Topology;
        bool isOnly = active != null && active.Paths.Length == 1 && monitor != null && monitor.Active;
        bool extendsAll = arrangement == DisplayConfiguration.Extend && active.Paths.Length == monitors.Items.Count;
        extend.Enabled = ready && monitors.Items.Count > 1 && !extendsAll;
        clone.Enabled = ready && monitors.Items.Count > 1 && arrangement != DisplayConfiguration.Clone;
        single.Enabled = ready && monitor != null && !isOnly;
        extend.Selected = extendsAll;
        clone.Selected = arrangement == DisplayConfiguration.Clone;
        single.Selected = isOnly;
        openCast.Enabled = ready;
        openCast.Text = mirror == null ? "投屏" : "投屏中";
        openArrangement.Enabled = ready;
        openCombination.Enabled = ready && monitors.Items.Count > 1;
        openDisplay.Enabled = !busy;
        openDisplay.Selected = !arrangementTools.Visible && !combination.Visible && !castTools.Visible && !shortcutTools.Visible;
        openArrangement.Selected = arrangementTools.Visible;
        openCombination.Selected = combination.Visible;
        openCast.Selected = castTools.Visible;
        openShortcuts.Enabled = shortcut != null && !busy && !confirming;
        openShortcuts.Selected = shortcutTools.Visible;
        monitors.Enabled = ready && monitors.Items.Count > 0;
        directions.Enabled = ready && directions.Items.Count > 0;
        resolutions.Enabled = ready && resolutions.Items.Count > 0;
        rates.Enabled = ready && rates.Items.Count > 0;
        var choice = rates.SelectedItem as RefreshChoice;
        apply.Enabled = ready && choice != null && !DisplayModes.SameMode(currentMode, choice.Mode);
        close.Enabled = paletteButton.Enabled = systemSettings.Enabled = !busy && !confirming;
        apply.Visible = systemSettings.Visible = !confirming && !arrangementTools.Visible && !combination.Visible && !castTools.Visible;
        cancel.Visible = keep.Visible = confirming;
        cancel.Enabled = keep.Enabled = confirming && !busy;
        notice.ForeColor = confirming ? ScreenTheme.Notice : ScreenTheme.Muted;
        UpdateArrangementActions();
    }

    private async void ApplyTopology(uint topology)
    {
        if (topology == DisplayConfiguration.Clone)
        {
            await ApplyChange(layout => DisplayConfiguration.SetTopology(topology), layout => layout.Topology == topology);
            return;
        }

        var selection = monitors.Items.Cast<MonitorChoice>().Select(choice => choice.Path.TargetKey).ToArray();
        await ApplyChange(layout =>
        {
            if (layout.Topology != DisplayConfiguration.Extend)
            {
                try
                {
                    DisplayConfiguration.SetTopology(topology);
                    layout = DisplayConfiguration.Read(false);
                    if (layout.ShowsSelection(selection))
                        return;
                }
                catch (Win32Exception)
                {
                    // Windows' saved Extend layout might omit a currently connected screen.
                }
            }
            DisplayConfiguration.ShowSelection(layout, selection);
        }, layout => layout.ShowsSelection(selection));
    }

    private void OpenCombination()
    {
        RefreshDisplay();
        if (monitors.Items.Count < 2)
            return;
        FillCombinationRows();
        ShowTools(combination);
    }

    private void OpenArrangement()
    {
        ShowTools(arrangementTools);
        RefreshDisplay();
    }

    private void UpdateArrangementActions()
    {
        bool editing = arrangementMap.HasChanges && !busy && previous == null;
        applyArrangement.Enabled = resetArrangement.Enabled = editing;
        arrangementStatus.Visible = arrangementMap.HasScreens;
    }

    private async Task ApplyArrangement()
    {
        if (!arrangementMap.HasChanges)
            return;
        var original = arrangementMap.OriginalAreas();
        var positions = arrangementMap.Positions();
        arrangementMap.ResetLayout();
        ShowTools(null);
        await ApplyChange(layout => DisplayConfiguration.SetLayout(DisplayConfiguration.MoveScreens(layout, original, positions), false),
            layout => DisplayConfiguration.HasPositions(layout, positions));
    }

    private void FillCombinationRows()
    {
        combinationRows.SuspendLayout();
        while (combinationRows.Controls.Count > 0)
            combinationRows.Controls[0].Dispose();
        int scrollInset = monitors.Items.Count > 4 ? SystemInformation.VerticalScrollBarWidth : 0;
        foreach (MonitorChoice monitor in monitors.Items)
        {
            var row = new DisplayCheckBox(scale) {
                Text = monitor.Title,
                Tag = monitor.Path.TargetKey,
                Checked = monitor.Active,
                Size = new Size(combinationRows.Width - scrollInset, Pixels(36)),
                Margin = new Padding(0, 0, 0, Pixels(6))
            };
            row.CheckedChanged += (sender, e) => UpdateCombination();
            combinationRows.Controls.Add(row);
        }
        combinationRows.ResumeLayout();
        UpdateCombination();
    }

    private void ShowTools(Panel page)
    {
        displayTools.Visible = page == null;
        arrangementTools.Visible = page == arrangementTools;
        combination.Visible = page == combination;
        castTools.Visible = page == castTools;
        shortcutTools.Visible = page == shortcutTools;
        if (page != shortcutTools)
            shortcutTools.CancelRecording();
        if (page != null)
            page.BringToFront();
        UpdateActions();
    }

    private DisplayConfiguration.Endpoint[] CheckedDisplays()
    {
        return combinationRows.Controls.OfType<DisplayCheckBox>()
            .Where(box => box.Checked).Select(box => (DisplayConfiguration.Endpoint)box.Tag).ToArray();
    }

    private void UpdateCombination()
    {
        var selection = CheckedDisplays();
        combinationCount.Text = "已选 " + selection.Length + " / " + combinationRows.Controls.Count + " 块";
        applyCombination.Enabled = selection.Length != 0 && active != null && !active.ShowsSelection(selection);
    }

    private async Task ApplyCombination()
    {
        var selection = CheckedDisplays();
        if (selection.Length == 0)
            return;
        ShowTools(null);
        await ApplyChange(layout => DisplayConfiguration.ShowSelection(layout, selection),
            layout => layout.ShowsSelection(selection));
    }

    private void OpenCast()
    {
        ShowTools(castTools);
        RefreshDisplay();
        UpdateCastActions();
    }

    private void PopulateCastSources()
    {
        var lastSource = castSource.SelectedItem as MonitorChoice;
        var preferred = mirror != null ? (DisplayConfiguration.Endpoint?)mirror.SourceKey
            : lastSource == null ? null : (DisplayConfiguration.Endpoint?)lastSource.Path.TargetKey;
        var connected = monitors.Items.Cast<MonitorChoice>().Where(choice => choice.Active).ToArray();
        var selected = connected.FirstOrDefault(choice => preferred.HasValue && choice.Path.TargetKey.Equals(preferred.Value));
        if (selected == null && mirror == null)
            selected = connected.FirstOrDefault();
        updating = true;
        try
        {
            castSource.SetItems(connected, selected);
        }
        finally
        {
            updating = false;
        }
        UpdateCastActions();
    }

    private void ChangeCastSource()
    {
        castMessage = null;
        var source = castSource.SelectedItem as MonitorChoice;
        if (mirror != null && source != null)
        {
            try
            {
                mirror.SwitchSource(source.Path.TargetKey);
            }
            catch (InvalidOperationException error)
            {
                castMessage = error.Message;
            }
            catch (Win32Exception)
            {
                castMessage = "暂时读不到显示设置，请重试。";
            }
        }
        if (castMessage != null)
            PopulateCastSources();
        else
            UpdateCastActions();
    }

    private void UpdateCastActions()
    {
        bool running = mirror != null;
        castToggle.Text = running ? "停止投屏" : "开始投屏";
        castSource.Enabled = castSource.Items.Count > 0;
        var from = castSource.SelectedItem as MonitorChoice;
        castToggle.Enabled = running || from != null;
        castStatus.Text = castMessage ?? (running
            ? (from == null ? "投屏中" : "投屏中 · 来源 " + from.Number + " 号屏")
            : (from == null ? "请先开启一块屏幕。" : "小窗可跨屏拖动，下次会记住位置。"));
    }

    private void ToggleCast()
    {
        if (mirror != null)
        {
            mirror.Close();
            return;
        }
        var source = castSource.SelectedItem as MonitorChoice;
        if (source == null)
            return;
        castMessage = null;
        var placementPath = Path.Combine(Path.GetDirectoryName(palettePath), "mirror-window.txt");
        var window = new MirrorWindow(source.Path.TargetKey, scale, placementPath);
        try
        {
            window.Start(Handle, Owner == null ? Handle : Owner.Handle);
            mirror = window;
            window.FormClosed += (sender, e) =>
            {
                mirror = null;
                castMessage = window.StopMessage;
                RefreshDisplay();
                UpdateCastActions();
            };
            UpdateCastActions();
            UpdateActions();
            CloseRequested(this, EventArgs.Empty);
        }
        catch (InvalidOperationException error)
        {
            window.Dispose();
            castMessage = error.Message;
            UpdateCastActions();
        }
        catch (Win32Exception)
        {
            window.Dispose();
            castMessage = "暂时读不到显示设置，请重试。";
            UpdateCastActions();
        }
    }

    private async void ApplySingle()
    {
        var selected = SelectedMonitor;
        if (selected == null)
            return;
        var target = selected.Path.TargetKey;
        await ApplyChange(layout => DisplayConfiguration.ShowOnly(layout, target),
            layout => layout.Paths.Length == 1 && layout.Paths[0].TargetKey.Equals(target));
    }

    private async void ApplyMode()
    {
        var selected = SelectedMonitor;
        var choice = rates.SelectedItem as RefreshChoice;
        if (selected == null || choice == null)
            return;
        var target = selected.Path.TargetKey;
        var requested = choice.Mode;
        await ApplyChange(layout => DisplayModes.Change(DeviceFor(layout, target), requested),
            layout => DisplayModes.SameMode(DisplayModes.ReadCurrent(DeviceFor(layout, target)), requested));
    }

    private static string DeviceFor(DisplayConfiguration.Layout layout, DisplayConfiguration.Endpoint target)
    {
        var path = layout.Paths.FirstOrDefault(item => item.TargetKey.Equals(target));
        var device = (path.Flags & 1) == 0 ? null : path.ScreenName;
        if (device == null)
            throw new InvalidOperationException("这块屏幕已关闭或拔出。");
        return device;
    }

    private async Task ApplyChange(Action<DisplayConfiguration.Layout> change,
        Func<DisplayConfiguration.Layout, bool> verify)
    {
        if (busy || previous != null)
            return;
        busy = true;
        notice.Text = "正在应用…";
        UpdateActions();
        try
        {
            previous = await Task.Run(() =>
            {
                var original = DisplayConfiguration.Read(false);
                try
                {
                    change(original);
                    if (!verify(DisplayConfiguration.Read(false)))
                        throw new InvalidOperationException("Windows 未能切换到所选设置。");
                }
                catch
                {
                    try
                    {
                        DisplayConfiguration.SetLayout(original, true);
                    }
                    catch (Win32Exception error)
                    {
                        throw new InvalidOperationException("切换失败且无法恢复，请打开系统显示设置。", error);
                    }
                    throw;
                }
                return original;
            });
            confirmationClock.Restart();
            notice.Text = "15 秒后自动恢复，确认保留更改？";
            confirmationTimer.Start();
        }
        catch (Exception error)
        {
            notice.Text = FormatError(error);
        }
        finally
        {
            busy = false;
            RefreshDisplay();
            DisplayChanged(this, EventArgs.Empty);
        }
    }

    private async void ConfirmTick(object sender, EventArgs e)
    {
        int seconds = (int)Math.Ceiling(15 - confirmationClock.Elapsed.TotalSeconds);
        if (seconds <= 0)
            await CompleteChange(false);
        else
            notice.Text = seconds + " 秒后自动恢复，确认保留更改？";
    }

    private async Task CompleteChange(bool accept)
    {
        if (previous == null || busy)
            return;
        var original = previous;
        busy = true;
        confirmationTimer.Stop();
        notice.Text = accept ? "正在保存…" : "正在恢复…";
        UpdateActions();
        try
        {
            await Task.Run(() => DisplayConfiguration.SetLayout(accept ? DisplayConfiguration.Read(false) : original, true));
            notice.Text = accept ? "已保存显示设置。" : "已恢复原来的显示设置。";
            previous = null;
        }
        catch (Exception error)
        {
            if (accept)
            {
                confirmationClock.Restart();
                confirmationTimer.Start();
                notice.Text = "保存失败，可点「恢复原设置」。";
            }
            else
            {
                previous = null;
                notice.Text = "恢复失败，请打开系统显示设置。";
            }
            Debug.WriteLine(error);
        }
        finally
        {
            busy = false;
            RefreshDisplay();
            DisplayChanged(this, EventArgs.Empty);
        }
    }

    private void OpenWindowsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            CloseRequested(this, EventArgs.Empty);
        }
        catch (Win32Exception error)
        {
            notice.Text = FormatError(error);
        }
    }

    private static string FormatError(Exception error)
    {
        var native = error as Win32Exception;
        return native == null ? error.Message : "Windows 未能应用该设置（" + native.NativeErrorCode + "）。";
    }

    private Label AddLabel(string text, int x, int y, int width, int height, Control parent = null)
    {
        var label = new GlassLabel { Text = text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
        Place(label, x, y, width, height, parent);
        return label;
    }

    private void AddFieldLabel(string text, int x, int y, int width, Control page)
    {
        AddLabel(text, x, y, width, 19, page).Font = fieldLabelFont;
    }

    private PanelButton AddButton(string text, int x, int y, int width, int height,
        PanelButton.Appearance appearance, ScreenTheme.Symbol symbol, EventHandler click, Control parent = null)
    {
        var button = new PanelButton(scale, appearance, symbol) { Text = text };
        button.Click += click;
        Place(button, x, y, width, height, parent);
        return button;
    }

    private PanelPicker AddCombo(string accessibleName, int x, int y, int width, Control parent = null)
    {
        var combo = new PanelPicker(scale) { AccessibleName = accessibleName };
        Place(combo, x, y, width, 36, parent);
        return combo;
    }

    private Rectangle ScaledRect(int x, int y, int width, int height)
    {
        return new Rectangle(Pixels(x), Pixels(y), Pixels(width), Pixels(height));
    }

    private void Place(Control control, int x, int y, int width, int height, Control parent = null)
    {
        control.SetBounds(Pixels(x), Pixels(y), Pixels(width), Pixels(height));
        (parent ?? this).Controls.Add(control);
    }

    private int Pixels(int value) { return (int)Math.Round(value * scale); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            confirmationTimer.Dispose();
            chooser.Dispose();
            SaveAppearanceIfPending();
            appearanceTimer.Dispose();
            glassPreviewTimer.Dispose();
            frame.Dispose();
            glass.Dispose();
            if (mirror != null)
                mirror.Dispose();
            fieldLabelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
