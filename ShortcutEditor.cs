using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ShortcutEditor : Panel
{
    private readonly GlobalShortcut shortcut;
    private readonly PanelButton record, defaults, save;
    private readonly Label help, status;
    private readonly float scale;
    private Keys pending;

    internal bool Recording { get; private set; }

    internal ShortcutEditor(float scale, GlobalShortcut shortcut, Font heading)
    {
        this.scale = scale;
        this.shortcut = shortcut;
        BackColor = Color.Transparent;
        Visible = false;
        var title = LabelAt("显示 / 隐藏悬浮球", 0, 0, 372, 22);
        title.Font = heading;
        record = ButtonAt("", 0, 37, 372, 41, PanelButton.Appearance.Secondary,
            ScreenTheme.Symbol.Keyboard, (sender, e) =>
        {
            Recording = true;
            status.Text = "";
            record.Focus();
            UpdateControls();
        });
        record.Font = heading;
        record.AccessibleName = "录入显示隐藏快捷键";
        help = LabelAt("", 0, 89, 372, 34);
        help.ForeColor = ScreenTheme.Muted;
        status = LabelAt("", 0, 137, 372, 31);
        defaults = ButtonAt("恢复默认", 0, 232, 108, 34, PanelButton.Appearance.Quiet,
            ScreenTheme.Symbol.None, (sender, e) =>
        {
            pending = GlobalShortcut.Default;
            Recording = false;
            status.Text = "";
            UpdateControls();
        });
        save = ButtonAt("保存快捷键", 250, 232, 122, 34, PanelButton.Appearance.Primary,
            ScreenTheme.Symbol.None, (sender, e) => Save());
    }

    internal void Begin()
    {
        if (shortcut == null)
            return;
        pending = shortcut.Current;
        Recording = false;
        ShowStatus(shortcut.Registered ? "" : "当前组合无法注册，请选一个可用快捷键。", !shortcut.Registered);
        UpdateControls();
    }

    internal void CancelRecording()
    {
        if (!Recording)
            return;
        Recording = false;
        UpdateControls();
    }

    internal bool Record(Keys keyData)
    {
        if (!Recording)
            return false;
        if (keyData == Keys.Escape)
        {
            CancelRecording();
            return true;
        }
        var key = keyData & Keys.KeyCode;
        if (GlobalShortcut.ModifierOnly(key))
            return true;
        if (!GlobalShortcut.IsValid(keyData))
        {
            ShowStatus(key == Keys.F12 ? "F12 是系统保留键，请换一个。"
                : "用 Ctrl、Alt 或 Shift 搭配另一个键，也可用功能键。", true);
            return true;
        }
        pending = keyData;
        Recording = false;
        ShowStatus("", false);
        UpdateControls();
        return true;
    }

    private void Save()
    {
        bool stored;
        if (!shortcut.Apply(pending, out stored))
            ShowStatus("这个组合已被占用或由系统保留，请换一个。", true);
        else
            ShowStatus(stored ? "快捷键已保存。" : "快捷键已启用，本次无法保存到磁盘。", !stored);
        UpdateControls();
    }

    private void UpdateControls()
    {
        record.Text = Recording ? "按下新的快捷键…" : GlobalShortcut.Format(pending);
        record.Selected = Recording;
        help.Text = Recording ? "按 Esc 取消录入。" : "点上方录入新组合。快捷键在其他窗口中也可用。";
        defaults.Enabled = !Recording && (pending != GlobalShortcut.Default || shortcut.Current != GlobalShortcut.Default);
        save.Enabled = !Recording && (!shortcut.Registered || pending != shortcut.Current);
    }

    private void ShowStatus(string message, bool warning)
    {
        status.Text = message;
        status.ForeColor = warning ? ScreenTheme.Notice : ScreenTheme.Accent;
    }

    private Label LabelAt(string text, int x, int y, int width, int height)
    {
        var label = new Label { Text = text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        Place(label, x, y, width, height);
        return label;
    }

    private PanelButton ButtonAt(string text, int x, int y, int width, int height,
        PanelButton.Appearance style, ScreenTheme.Symbol icon, EventHandler click)
    {
        var button = new PanelButton(scale, style, icon) { Text = text };
        button.Click += click;
        Place(button, x, y, width, height);
        return button;
    }

    private void Place(Control control, int x, int y, int width, int height)
    {
        control.Bounds = new Rectangle(Pixels(x), Pixels(y), Pixels(width), Pixels(height));
        Controls.Add(control);
    }

    private int Pixels(int value) { return (int)Math.Round(value * scale); }
}
