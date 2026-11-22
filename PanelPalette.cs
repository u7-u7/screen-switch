using System;
using System.Drawing;

internal sealed class PanelPalette
{
    internal static readonly PanelPalette[] Options = {
        new PanelPalette("graphite", "石墨", 0x181D21, 0xADDDCC),
        new PanelPalette("moss", "苔绿", 0x19221D, 0xBDDBA9),
        new PanelPalette("blue", "夜蓝", 0x171F2D, 0xB6CDF9),
        new PanelPalette("violet", "雾紫", 0x211C2A, 0xD9C6F2),
        new PanelPalette("sand", "暖砂", 0x26201C, 0xEFCDAA)
    };

    internal readonly string Id, Name;
    internal readonly Color Canvas, Field, Hover, Border, Accent, AccentSurface, AccentInk;

    private PanelPalette(string id, string name, int canvas, int accent)
    {
        Id = id;
        Name = name;
        Canvas = Rgb(canvas);
        Accent = Rgb(accent);
        var neutral = Mix(Color.FromArgb(218, 227, 230), Accent, .27f);
        Field = Mix(Canvas, neutral, .075f);
        Hover = Mix(Canvas, neutral, .145f);
        Border = Mix(Canvas, neutral, .205f);
        AccentSurface = Mix(Canvas, Accent, .235f);
        AccentInk = Mix(Canvas, Accent, .055f);
    }

    private static Color Rgb(int value)
    {
        return Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    internal static Color Mix(Color first, Color second, float amount)
    {
        return Color.FromArgb(
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount));
    }
}
