using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class GlobalShortcut
{
    internal const Keys Default = Keys.Control | Keys.Alt | Keys.E;
    private const int FirstId = 0x5345, SecondId = 0x5346;
    private const uint NoRepeat = 0x4000;
    private const Keys SupportedModifiers = Keys.Control | Keys.Alt | Keys.Shift;
    private readonly string path;
    private IntPtr window;
    private int registeredId;

    internal Keys Current { get; private set; }
    internal bool Registered { get { return registeredId != 0; } }

    internal GlobalShortcut(string path = null)
    {
        this.path = path ?? Path.Combine(Path.GetDirectoryName(AppearancePreferences.DefaultPath), "shortcut.txt");
        Current = Load(this.path);
    }

    internal void Attach(IntPtr handle)
    {
        Detach();
        window = handle;
        if (!Register(Current) && Current != Default)
            Register(Default);
    }

    internal void Detach()
    {
        if (Registered)
            UnregisterHotKey(window, registeredId);
        registeredId = 0;
        window = IntPtr.Zero;
    }

    internal bool Matches(IntPtr id)
    {
        return Registered && id == new IntPtr(registeredId);
    }

    internal bool Apply(Keys keys, out bool saved)
    {
        saved = false;
        if (!IsValid(keys) || !Register(keys))
            return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ((int)keys).ToString(CultureInfo.InvariantCulture));
            saved = true;
        }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is UnauthorizedAccessException))
                throw;
            Debug.WriteLine(error);
        }
        return true;
    }

    private bool Register(Keys keys)
    {
        if (Registered && keys == Current)
            return true;
        if (window == IntPtr.Zero)
            return false;
        int nextId = registeredId == FirstId ? SecondId : FirstId;
        uint flags = NoRepeat;
        if ((keys & Keys.Alt) != 0) flags |= 0x0001;
        if ((keys & Keys.Control) != 0) flags |= 0x0002;
        if ((keys & Keys.Shift) != 0) flags |= 0x0004;

        // Acquire the replacement before releasing the hotkey that can reveal the ball.
        if (!RegisterHotKey(window, nextId, flags, (uint)(keys & Keys.KeyCode)))
            return false;
        if (Registered)
            UnregisterHotKey(window, registeredId);
        registeredId = nextId;
        Current = keys;
        return true;
    }

    private static Keys Load(string path)
    {
        try
        {
            int value;
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) && IsValid((Keys)value))
                return (Keys)value;
        }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is UnauthorizedAccessException))
                throw;
            Debug.WriteLine(error);
        }
        return Default;
    }

    internal static bool ModifierOnly(Keys key)
    {
        return key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu
            || key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey
            || key == Keys.LMenu || key == Keys.RMenu || key == Keys.LWin || key == Keys.RWin;
    }

    internal static bool IsValid(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        bool function = key >= Keys.F1 && key <= Keys.F24;
        return key >= Keys.Back && (int)key <= 0xfe && key != Keys.F12 && !ModifierOnly(key)
            && (keys & ~(Keys.KeyCode | SupportedModifiers)) == 0
            && ((keys & SupportedModifiers) != 0 || function);
    }

    internal static string Format(Keys keys)
    {
        string result = "";
        if ((keys & Keys.Control) != 0) result += "Ctrl + ";
        if ((keys & Keys.Alt) != 0) result += "Alt + ";
        if ((keys & Keys.Shift) != 0) result += "Shift + ";
        var key = keys & Keys.KeyCode;
        if (key >= Keys.D0 && key <= Keys.D9)
            return result + ((int)key - (int)Keys.D0);
        return result + new KeysConverter().ConvertToString(key);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
