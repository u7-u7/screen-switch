using System.Drawing;
using System.Drawing.Drawing2D;

internal static class AppLogo
{
    internal static readonly Color Background = Color.FromArgb(23, 27, 29);

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
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 64f, bounds.Height / 64f);
        using (var fill = new SolidBrush(Background))
            graphics.FillEllipse(fill, 2, 2, 60, 60);
        using (var upper = new GraphicsPath())
        using (var lower = new GraphicsPath())
        using (var stroke = new Pen(Color.FromArgb(165, 189, 177), 3.75f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            upper.AddLine(16, 32, 16, 26);
            upper.AddArc(16, 19, 14, 14, 180, 90);
            upper.AddLines(new[] { new Point(23, 19), new Point(36, 19), new Point(43, 26) });
            lower.AddLine(48, 32, 48, 38);
            lower.AddArc(34, 31, 14, 14, 0, 90);
            lower.AddLines(new[] { new Point(41, 45), new Point(28, 45), new Point(21, 38) });
            graphics.DrawPath(stroke, upper);
            stroke.Color = Color.FromArgb(243, 244, 238);
            graphics.DrawPath(stroke, lower);
        }
        if (highlighted)
            using (var edge = new Pen(Color.FromArgb(155, 165, 189, 177), 1.6f))
                graphics.DrawEllipse(edge, 3, 3, 58, 58);
        graphics.Restore(state);
    }
}
