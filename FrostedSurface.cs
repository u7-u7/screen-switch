using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal sealed class FrostedSurface : IDisposable
{
    private Bitmap blurred;
    private Bitmap scaledBackdrop;
    internal Bitmap Image { get; private set; }

    internal void Capture(Rectangle bounds, float scale)
    {
        // Capture only the panel rectangle before the panel is shown.
        try
        {
            using (var sample = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                Sample(sample, scale);
            }
        }
        catch (Win32Exception)
        {
            Clear();
        }
    }

    internal void Sample(Bitmap source, float scale)
    {
        if (scaledBackdrop != null)
        {
            scaledBackdrop.Dispose();
            scaledBackdrop = null;
        }
        if (blurred != null)
            blurred.Dispose();
        var divisor = Math.Max(4 * scale, Math.Max(source.Width, source.Height) / 112f);
        int width = Math.Max(1, (int)Math.Ceiling(source.Width / divisor));
        int height = Math.Max(1, (int)Math.Ceiling(source.Height / divisor));
        blurred = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(blurred))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        var pixels = new Color[width * height];
        var buffer = new Color[pixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = blurred.GetPixel(x, y);

        for (int pass = 0; pass < 3; pass++)
        {
            Soften(pixels, buffer, width, height, true);
            Soften(buffer, pixels, width, height, false);
        }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                blurred.SetPixel(x, y, pixels[y * width + x]);
    }

    private static void Soften(Color[] source, Color[] output, int width, int height, bool horizontal)
    {
        const int radius = 4;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int axis = horizontal ? x : y;
                int first = Math.Max(0, axis - radius);
                int last = Math.Min((horizontal ? width : height) - 1, axis + radius);
                int red = 0, green = 0, blue = 0;
                for (int offset = first; offset <= last; offset++)
                {
                    var pixel = source[horizontal ? y * width + offset : offset * width + x];
                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                }
                int weight = last - first + 1;
                output[y * width + x] = Color.FromArgb(red / weight, green / weight, blue / weight);
            }
        }
    }

    internal void Tint(Size size, Color tint, double transparency)
    {
        if (Image == null || Image.Size != size)
        {
            if (Image != null)
                Image.Dispose();
            Image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        }
        using (var graphics = Graphics.FromImage(Image))
        {
            graphics.Clear(tint);
            var bounds = new Rectangle(Point.Empty, size);
            if (blurred != null && transparency > 0)
            {
                if (scaledBackdrop == null || scaledBackdrop.Size != size)
                {
                    if (scaledBackdrop != null)
                        scaledBackdrop.Dispose();
                    scaledBackdrop = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                    using (var backdrop = Graphics.FromImage(scaledBackdrop))
                    {
                        backdrop.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        backdrop.DrawImage(blurred, bounds);
                    }
                }
                graphics.DrawImageUnscaled(scaledBackdrop, 0, 0);
                int opacity = 255 - (int)Math.Round(transparency * 255d / 100);
                using (var veil = new SolidBrush(Color.FromArgb(opacity, tint)))
                    graphics.FillRectangle(veil, bounds);
            }
            using (var glow = new LinearGradientBrush(bounds, Color.FromArgb(16, 243, 251, 250),
                Color.FromArgb(17, 11, 19, 22), LinearGradientMode.Vertical))
                graphics.FillRectangle(glow, bounds);
        }
    }

    internal void Clear()
    {
        if (blurred != null)
            blurred.Dispose();
        if (Image != null)
            Image.Dispose();
        if (scaledBackdrop != null)
            scaledBackdrop.Dispose();
        blurred = null;
        scaledBackdrop = null;
        Image = null;
    }

    public void Dispose() { Clear(); }
}
