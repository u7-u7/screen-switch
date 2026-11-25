using System.Drawing;
using System.Drawing.Drawing2D;

internal static class AppLogo
{
    internal static readonly Color Background = Color.FromArgb(36, 83, 196);
    private static readonly Bitmap Logo = Load();

    internal static GraphicsPath Outline(RectangleF bounds)
    {
        // Match the circle in the 64 × 64 SVG.
        var inset = bounds;
        inset.Inflate(-bounds.Width / 32f, -bounds.Height / 32f);
        var path = new GraphicsPath();
        path.AddEllipse(inset);
        return path;
    }

    internal static void Draw(Graphics graphics, RectangleF bounds, bool highlighted = false)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 64f, bounds.Height / 64f);
        graphics.DrawImage(Logo, new Rectangle(0, 0, 64, 64), 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel);
        if (highlighted)
            using (var edge = new Pen(Color.FromArgb(155, 191, 228, 255), 1.6f))
                graphics.DrawEllipse(edge, 3, 3, 58, 58);
        graphics.Restore(state);
    }

    private static Bitmap Load()
    {
        using (var stream = typeof(AppLogo).Assembly.GetManifestResourceStream("ScreenSwitchLogo.png"))
        using (var image = Image.FromStream(stream))
            return new Bitmap(image);
    }
}
