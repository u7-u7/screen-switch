using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class PanelFrame : NativeWindow, IDisposable
{
    private const uint NoActivate = 0x0010;
    private const uint ShowWindowFlag = 0x0040;
    private Size renderedSize;
    private Point renderedPosition;
    private Color renderedColor;
    private Bitmap renderedBackground;
    private Bitmap shadow;
    private Bitmap surface;
    private byte[] pixelBuffer;
    private float shadowScale;
    private bool shown;

    internal static float CornerRadius(float scale) { return 25 * scale; }
    internal static int Padding(float scale) { return (int)Math.Ceiling(27 * scale); }

    internal bool Follow(Form panel, float scale, Bitmap background, bool materialChanged)
    {
        if (Handle == IntPtr.Zero)
        {
            const int popup = unchecked((int)0x80000000);
            const int layeredToolWindow = 0x00080000 | 0x08000000 | 0x00000080 | 0x00000020 | 0x00000008;
            CreateHandle(new CreateParams { Style = popup, ExStyle = layeredToolWindow });
            renderedSize = Size.Empty;
        }

        int padding = Padding(scale);
        var position = new Point(panel.Left - padding, panel.Top - padding);
        var size = new Size(panel.Width + padding * 2, panel.Height + padding * 2);
        var color = panel.BackColor;
        bool reposition = !shown || renderedPosition != position || renderedSize != size;
        if (materialChanged || renderedSize != size || renderedColor != color || renderedBackground != background)
        {
            Render(panel.Size, scale, color, background);
            if (!Upload(Handle, position, reposition))
            {
                Hide();
                return false;
            }
            renderedSize = size;
            renderedColor = color;
            renderedBackground = background;
        }
        if (reposition)
        {
            shown = SetWindowPos(Handle, panel.Handle, position.X, position.Y, size.Width, size.Height,
                NoActivate | ShowWindowFlag);
            renderedPosition = position;
            if (!shown)
                Hide();
        }
        return shown;
    }

    internal void Hide()
    {
        shown = false;
        if (Handle != IntPtr.Zero)
            ShowWindow(Handle, 0);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
            DestroyHandle();
        if (shadow != null)
            shadow.Dispose();
        if (surface != null)
            surface.Dispose();
    }

    private void Render(Size bodySize, float scale, Color canvas, Bitmap background)
    {
        int pad = Padding(scale);
        var size = new Size(bodySize.Width + 2 * pad, bodySize.Height + 2 * pad);
        var body = new RectangleF(pad + .5f, pad + .5f, bodySize.Width - 1, bodySize.Height - 1);
        float radius = CornerRadius(scale);
        if (shadow == null || shadow.Size != size || shadowScale != scale)
        {
            if (shadow != null)
                shadow.Dispose();
            if (surface != null)
                surface.Dispose();
            shadow = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            surface = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            shadowScale = scale;
            using (var graphics = Graphics.FromImage(shadow))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (int spread = 19; spread >= 1; spread--)
                {
                    var shade = body;
                    shade.Offset(0, 5 * scale);
                    shade.Inflate(spread * scale, spread * scale);
                    int strength = 20 - spread;
                    int opacity = 1 + strength * strength / 60;
                    ScreenTheme.Surface(graphics, shade, radius + spread * scale,
                        Color.FromArgb(opacity, 7, 11, 14), Color.Transparent);
                }
            }
        }
        using (var graphics = Graphics.FromImage(surface))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(shadow, 0, 0);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = ScreenTheme.Rounded(body, radius))
            using (Brush material = background == null ? (Brush)new SolidBrush(canvas)
                : new TextureBrush(background, WrapMode.Clamp))
            using (var border = new Pen(Color.FromArgb(56, ScreenTheme.Text)))
            {
                var texture = material as TextureBrush;
                if (texture != null)
                    texture.TranslateTransform(pad, pad);
                graphics.FillPath(material, outline);
                graphics.DrawPath(border, outline);
            }
        }
    }

    private bool Upload(IntPtr window, Point position, bool reposition)
    {
        var image = surface;
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return false;
        IntPtr dib = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            var header = new BitmapHeader
            {
                Length = 40,
                Width = image.Width,
                Height = -image.Height,
                Planes = 1,
                Bits = 32
            };
            IntPtr pixels;
            dib = CreateDIBSection(dc, ref header, 0, out pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
                return false;
            old = SelectObject(dc, dib);
            if (old == IntPtr.Zero || old == new IntPtr(-1))
                return false;
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int rowLength = image.Width * 4;
                if (data.Stride == rowLength)
                {
                    int length = rowLength * image.Height;
                    if (pixelBuffer == null || pixelBuffer.Length != length)
                        pixelBuffer = new byte[length];
                    Marshal.Copy(data.Scan0, pixelBuffer, 0, length);
                    Marshal.Copy(pixelBuffer, 0, pixels, length);
                }
                else
                {
                    var row = new byte[rowLength];
                    for (int y = 0; y < image.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, data.Stride * y), row, 0, rowLength);
                        Marshal.Copy(row, 0, IntPtr.Add(pixels, rowLength * y), rowLength);
                    }
                }
            }
            finally
            {
                image.UnlockBits(data);
            }

            var size = image.Size;
            var source = Point.Empty;
            var blend = new Blend { Opacity = 255, Format = 1 };
            return reposition
                ? UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, dc, ref source, 0, ref blend, 2)
                : UpdateLayeredContent(window, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, dc, ref source, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1))
                SelectObject(dc, old);
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Length;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int XResolution, YResolution;
        internal uint Colors, ImportantColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blend
    {
        internal byte Operation, Flags, Opacity, Format;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr source);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage,
        out IntPtr pixels, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point position, ref Size size,
        IntPtr dc, ref Point source, uint colorKey, ref Blend blend, uint flags);

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    private static extern bool UpdateLayeredContent(IntPtr window, IntPtr screen, IntPtr position, IntPtr size,
        IntPtr dc, ref Point source, uint colorKey, ref Blend blend, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
