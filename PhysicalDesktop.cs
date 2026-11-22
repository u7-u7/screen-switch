using System;
using System.Runtime.InteropServices;

internal struct PhysicalDesktop : IDisposable
{
    private static readonly IntPtr PerMonitorV2 = new IntPtr(-4);
    private readonly IntPtr previous;

    private PhysicalDesktop(IntPtr previous) { this.previous = previous; }

    internal static void EnableNativeDpi()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorV2))
                return;
        }
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }

    internal static float WindowScale(IntPtr window, float fallback)
    {
        try
        {
            uint dpi = GetDpiForWindow(window);
            return dpi > 0 ? dpi / 96f : fallback;
        }
        catch (EntryPointNotFoundException)
        {
            return fallback;
        }
    }

    internal static PhysicalDesktop Enter()
    {
        try
        {
            return new PhysicalDesktop(SetThreadDpiAwarenessContext(PerMonitorV2));
        }
        catch (EntryPointNotFoundException)
        {
            return default(PhysicalDesktop);
        }
    }

    public void Dispose()
    {
        if (previous != IntPtr.Zero)
            SetThreadDpiAwarenessContext(previous);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
