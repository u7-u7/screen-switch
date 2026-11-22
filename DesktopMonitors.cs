using System;
using System.Drawing;
using System.Runtime.InteropServices;

internal static class DesktopMonitors
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Monitor
    {
        internal int Size;
        internal Rect Display, Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Name;
    }

    internal static Monitor At(Point point, bool exact = false)
    {
        return Read(MonitorFromPoint(point, exact ? 0u : 2u));
    }

    internal static Monitor FromWindow(IntPtr window)
    {
        return Read(MonitorFromWindow(window, 2));
    }

    internal static bool IntersectsDesktop(Rectangle bounds)
    {
        var rectangle = new Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        return MonitorFromRect(ref rectangle, 0) != IntPtr.Zero;
    }

    internal static Monitor Find(string device, bool fallbackToPrimary = true)
    {
        Monitor found = default(Monitor), primary = default(Monitor);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, dc, rect, data) =>
        {
            var monitor = Read(handle);
            if ((monitor.Flags & 1) != 0)
                primary = monitor;
            if (device != null && monitor.Name == device)
                found = monitor;
            return true;
        }, IntPtr.Zero);
        return found.Name == null && fallbackToPrimary ? primary : found;
    }

    private static Monitor Read(IntPtr handle)
    {
        var info = new Monitor { Size = Marshal.SizeOf(typeof(Monitor)) };
        return handle != IntPtr.Zero && GetMonitorInfo(handle, ref info) ? info : default(Monitor);
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rectangle, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref Monitor info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint fallback);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint fallback);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint fallback);
}
