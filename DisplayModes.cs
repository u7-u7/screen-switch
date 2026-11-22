using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

internal static class DisplayModes
{
    private const uint Position = 0x20;
    private const uint Orientation = 0x80;
    private const uint ColorDepth = 0x40000;
    private const uint Width = 0x80000;
    private const uint Height = 0x100000;
    private const uint DisplayFlags = 0x200000;
    private const uint Frequency = 0x400000;
    private const uint RotatedModes = 4;

    [StructLayout(LayoutKind.Explicit, Size = 220)]
    internal struct Mode
    {
        [FieldOffset(68)] internal ushort Size;
        [FieldOffset(72)] internal uint Fields;
        [FieldOffset(76)] internal int X;
        [FieldOffset(80)] internal int Y;
        [FieldOffset(84)] internal uint Orientation;
        [FieldOffset(168)] internal uint ColorBits;
        [FieldOffset(172)] internal uint Width;
        [FieldOffset(176)] internal uint Height;
        [FieldOffset(180)] internal uint Flags;
        [FieldOffset(184)] internal uint Hertz;

        internal string Resolution { get { return Width + " × " + Height; } }
        internal string Rate { get { return Hertz > 1 ? Hertz + " Hz" : "默认"; } }
    }

    internal static Mode ReadCurrent(string device)
    {
        var mode = new Mode { Size = (ushort)Marshal.SizeOf(typeof(Mode)) };
        if (!EnumDisplaySettingsEx(device, -1, ref mode, 0))
            throw new InvalidOperationException("读不到这块屏幕的当前设置。");
        return mode;
    }

    internal static Mode[] ReadAvailable(string device, Mode current)
    {
        var modes = new List<Mode> { current };
        for (int index = 0; ; index++)
        {
            var mode = new Mode { Size = current.Size };
            if (!EnumDisplaySettingsEx(device, index, ref mode, RotatedModes))
                break;
            if (mode.ColorBits == current.ColorBits && mode.Orientation <= 3
                && mode.Flags == current.Flags && mode.Hertz > 1)
                modes.Add(mode);
        }
        return modes.OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.Hertz).ToArray();
    }

    internal static void Change(string device, Mode requested)
    {
        var current = ReadCurrent(device);
        requested.X = current.X;
        requested.Y = current.Y;
        requested.Fields = Position | Orientation | ColorDepth | Width | Height | DisplayFlags | Frequency;
        Check(ChangeDisplaySettingsEx(device, ref requested, IntPtr.Zero, 2, IntPtr.Zero));
        Check(ChangeDisplaySettingsEx(device, ref requested, IntPtr.Zero, 0, IntPtr.Zero));
    }

    internal static bool SameMode(Mode first, Mode second)
    {
        return first.Width == second.Width && first.Height == second.Height
            && first.Hertz == second.Hertz && first.Orientation == second.Orientation;
    }

    private static void Check(int code)
    {
        if (code != 0)
            throw new InvalidOperationException("Windows 未能应用这组显示设置（" + code + "）。");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string device, int index, ref Mode mode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string device, ref Mode mode, IntPtr window, uint flags, IntPtr parameter);
}
