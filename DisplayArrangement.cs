using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Endpoint = DisplayConfiguration.Endpoint;

internal sealed class DisplayArrangement : Control
{
    private sealed class Tile
    {
        internal Endpoint Target;
        internal int Number;
        internal Rectangle Original, Bounds;
    }

    private struct View
    {
        internal float Scale, X, Y;

        internal RectangleF DrawBounds(Rectangle area)
        {
            return new RectangleF(X + area.X * Scale, Y + area.Y * Scale, area.Width * Scale, area.Height * Scale);
        }
    }

    private readonly float scale;
    private Tile[] tiles = new Tile[0];
    private Tile primary, moving;
    private View dragView;
    private Point dragStart;
    private Rectangle preview;

    internal event EventHandler LayoutEdited = delegate { };
    internal bool HasScreens { get { return tiles.Length > 1; } }
    internal bool IsDragging { get { return moving != null; } }
    internal bool HasChanges
    {
        get
        {
            return primary != null && tiles.Any(tile => tile.Bounds.X - primary.Bounds.X != tile.Original.X - primary.Original.X
                || tile.Bounds.Y - primary.Bounds.Y != tile.Original.Y - primary.Original.Y);
        }
    }

    internal DisplayArrangement(float scale)
    {
        this.scale = scale;
        AccessibleName = "屏幕排列预览";
        AccessibleDescription = "拖动屏幕卡片，调整相对位置。";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void SetLayout(Dictionary<Endpoint, Rectangle> areas, Dictionary<Endpoint, int> numbers)
    {
        Rectangle area;
        if (areas.Count == tiles.Length && tiles.All(tile => areas.TryGetValue(tile.Target, out area)
            && area == tile.Original && tile.Number == numbers[tile.Target]))
            return;
        EndDrag(false);
        tiles = areas.Select(item => new Tile
        {
            Target = item.Key, Number = numbers[item.Key], Bounds = item.Value, Original = item.Value
        }).OrderBy(tile => tile.Number).ToArray();
        primary = tiles.FirstOrDefault(tile => tile.Original.Location == Point.Empty) ?? tiles.FirstOrDefault();
        AccessibleDescription = HasScreens ? "拖动屏幕卡片，调整相对位置。" : "开启至少两块扩展屏幕后可排列。";
        Invalidate();
    }

    internal Dictionary<Endpoint, Rectangle> OriginalAreas()
    {
        return tiles.ToDictionary(tile => tile.Target, tile => tile.Original);
    }

    internal Dictionary<Endpoint, Point> Positions()
    {
        return tiles.ToDictionary(tile => tile.Target, tile => new Point(tile.Bounds.X - primary.Bounds.X,
            tile.Bounds.Y - primary.Bounds.Y));
    }

    internal void ResetLayout()
    {
        EndDrag(false);
        foreach (var tile in tiles)
            tile.Bounds = tile.Original;
        Invalidate();
        LayoutEdited(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !HasScreens)
            return;
        dragView = GetView();
        moving = tiles.FirstOrDefault(tile => dragView.DrawBounds(tile.Bounds).Contains(e.Location));
        if (moving == null)
            return;
        preview = moving.Bounds;
        dragStart = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (moving == null)
        {
            var view = GetView();
            Cursor = HasScreens && tiles.Any(tile => view.DrawBounds(tile.Bounds).Contains(e.Location))
                ? Cursors.Hand : Cursors.Default;
            return;
        }
        int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
        var requested = moving.Bounds;
        var tolerance = SystemInformation.DragSize;
        if (Math.Abs(dx) >= tolerance.Width / 2 || Math.Abs(dy) >= tolerance.Height / 2)
        {
            requested.Offset((int)Math.Round(dx / dragView.Scale), (int)Math.Round(dy / dragView.Scale));
            var neighbors = tiles.Where(tile => tile != moving).Select(tile => tile.Bounds).ToArray();
            requested = Snap(requested, neighbors, (int)Math.Round(7 * scale / dragView.Scale)) ?? moving.Bounds;
        }
        if (preview != requested)
        {
            preview = requested;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            EndDrag(true);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
            EndDrag(false);
    }

    private void EndDrag(bool accept)
    {
        if (moving == null)
            return;
        bool changed = accept && moving.Bounds != preview;
        if (changed)
            moving.Bounds = preview;
        moving = null;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
        if (changed)
            LayoutEdited(this, EventArgs.Empty);
    }

    internal static Rectangle? Snap(Rectangle desired, Rectangle[] neighbors, int threshold)
    {
        var xMarks = neighbors.SelectMany(rect => new[] { rect.Left, rect.Right, rect.Left - desired.Width,
            rect.Right - desired.Width }).Distinct().ToArray();
        var yMarks = neighbors.SelectMany(rect => new[] { rect.Top, rect.Bottom, rect.Top - desired.Height,
            rect.Bottom - desired.Height }).Distinct().ToArray();
        desired.Location = new Point(SnapToMark(desired.X, xMarks, threshold), SnapToMark(desired.Y, yMarks, threshold));
        var candidates = new HashSet<Rectangle>();
        foreach (var neighbor in neighbors)
        {
            foreach (int y in AlongEdge(yMarks, desired.Y, neighbor.Top - desired.Height + 1, neighbor.Bottom - 1))
            {
                candidates.Add(new Rectangle(neighbor.Left - desired.Width, y, desired.Width, desired.Height));
                candidates.Add(new Rectangle(neighbor.Right, y, desired.Width, desired.Height));
            }
            foreach (int x in AlongEdge(xMarks, desired.X, neighbor.Left - desired.Width + 1, neighbor.Right - 1))
            {
                candidates.Add(new Rectangle(x, neighbor.Top - desired.Height, desired.Width, desired.Height));
                candidates.Add(new Rectangle(x, neighbor.Bottom, desired.Width, desired.Height));
            }
        }
        foreach (var candidate in candidates.OrderBy(rect => Distance(rect.Location, desired.Location)))
            if (DisplayConfiguration.CanKeepPositions(neighbors.Concat(new[] { candidate }).ToArray()))
                return candidate;
        return null;
    }

    private static IEnumerable<int> AlongEdge(int[] marks, int desired, int minimum, int maximum)
    {
        return marks.Where(mark => mark >= minimum && mark <= maximum)
            .Concat(new[] { Math.Max(minimum, Math.Min(maximum, desired)), minimum, maximum }).Distinct();
    }

    private static int SnapToMark(int value, int[] marks, int threshold)
    {
        return marks.Where(mark => Math.Abs(mark - value) <= threshold).OrderBy(mark => Math.Abs(mark - value))
            .DefaultIfEmpty(value).First();
    }

    private static double Distance(Point a, Point b)
    {
        double x = (double)a.X - b.X, y = (double)a.Y - b.Y;
        return x * x + y * y;
    }

    private View GetView()
    {
        if (!HasScreens || Width < 2 || Height < 2)
            return default(View);
        var extent = tiles.Select(tile => tile.Bounds).Aggregate(Rectangle.Union);
        // Leave room to see the dragged tile beyond the outside edges of the current layout.
        float margin = 16 * scale;
        float zoom = Math.Min((Width - margin * 2) / (extent.Width + tiles.Min(tile => tile.Bounds.Width) * .7f),
            (Height - margin * 2) / (extent.Height + tiles.Min(tile => tile.Bounds.Height) * .7f));
        return new View { Scale = zoom, X = Width / 2f - (extent.Left + extent.Width / 2f) * zoom,
            Y = Height / 2f - (extent.Top + extent.Height / 2f) * zoom };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        SettingsPanel.PaintBehindControl(this, graphics);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ScreenTheme.Surface(graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            15 * scale, ScreenTheme.GlassField, ScreenTheme.Hairline);
        if (!HasScreens)
        {
            TextRenderer.DrawText(graphics, "开启至少两块扩展屏幕后可排列。", Font, ClientRectangle, ScreenTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return;
        }
        var view = moving == null ? GetView() : dragView;
        var state = graphics.Save();
        graphics.SetClip(Rectangle.Inflate(ClientRectangle, -3, -3));
        foreach (var tile in tiles.Where(tile => tile != moving))
            PaintTile(graphics, tile, view.DrawBounds(tile.Bounds), false);
        if (moving != null)
            PaintTile(graphics, moving, view.DrawBounds(preview), true);
        graphics.Restore(state);
    }

    private void PaintTile(Graphics graphics, Tile tile, RectangleF bounds, bool dragged)
    {
        bounds.Inflate(-1.5f * scale, -1.5f * scale);
        if (bounds.Width < 2 || bounds.Height < 2)
            return;
        bool main = tile == primary;
        ScreenTheme.Surface(graphics, bounds, 9 * scale,
            main || dragged ? ScreenTheme.AccentSurface : ScreenTheme.Canvas, dragged ? ScreenTheme.Accent : ScreenTheme.Border);
        var title = tile.Number.ToString();
        if (main && bounds.Width >= 66 * scale)
            title += " · 主屏";
        TextRenderer.DrawText(graphics, title, Font, Rectangle.Round(bounds), dragged || main ? ScreenTheme.Accent : ScreenTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
