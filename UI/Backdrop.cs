using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PADLOck.UI;

// Весь фон окна собирается в одну картинку: фото, размытые подложки карточек, их заливка и рамки.
// Любой элемент рисует свой фон одним копированием куска этой картинки — без перерисовки родителей
// и без повторного смешивания, поэтому окно не мигает.
public sealed class Backdrop : IDisposable
{
    private const int BlurFactor = 12;

    private readonly Form _root;
    private readonly List<GlassPanel> _panels = new();
    private readonly Image? _source;
    private Bitmap? _base;
    private Bitmap? _blurred;
    private Bitmap? _composed;
    private Size _baseSize;
    private bool _pending;

    public Backdrop(Form root)
    {
        _root = root;
        _source = LoadEmbedded();
    }

    private static Image? LoadEmbedded()
    {
        using var stream = typeof(Backdrop).Assembly.GetManifestResourceStream("background.jpg");
        if (stream is null)
            return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    public void Register(GlassPanel panel)
    {
        _panels.Add(panel);
        panel.SizeChanged += (_, _) => RequestRebuild();
        panel.LocationChanged += (_, _) => RequestRebuild();
        panel.VisibleChanged += (_, _) => RequestRebuild();
    }

    // Много изменений раскладки подряд схлопываются в одну пересборку
    public void RequestRebuild()
    {
        if (_pending || !_root.IsHandleCreated)
            return;
        _pending = true;
        _root.BeginInvoke(() =>
        {
            _pending = false;
            Rebuild();
            _root.Invalidate(true);
        });
    }

    public void Rebuild()
    {
        var size = _root.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        if (_base is null || _baseSize != size)
            BuildBase(size);

        var composed = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(composed))
        {
            g.DrawImageUnscaled(_base!, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var panel in _panels)
            {
                if (!panel.Visible || !panel.IsHandleCreated)
                    continue;

                var r = _root.RectangleToClient(panel.RectangleToScreen(panel.ClientRectangle));
                if (r.Width <= 1 || r.Height <= 1)
                    continue;

                using var path = panel.Radius > 0
                    ? Theme.Rounded(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), panel.Radius)
                    : RectanglePath(r);

                var state = g.Save();
                g.SetClip(path);
                g.DrawImage(_blurred!, r, r, GraphicsUnit.Pixel);
                using (var tint = new SolidBrush(panel.Tint))
                    g.FillRectangle(tint, r);
                g.Restore(state);

                if (panel.HasBorder)
                {
                    using var pen = new Pen(Theme.GlassBorder);
                    g.DrawPath(pen, path);
                }
            }
        }

        _composed?.Dispose();
        _composed = composed;
    }

    public void Paint(Graphics g, Control target, Rectangle clip)
    {
        var bitmap = _composed;
        if (bitmap is null)
        {
            using var brush = new SolidBrush(Theme.BaseTop);
            g.FillRectangle(brush, clip);
            return;
        }

        var origin = target == _root ? Point.Empty : _root.PointToClient(target.PointToScreen(Point.Empty));
        var source = new Rectangle(clip.X + origin.X, clip.Y + origin.Y, clip.Width, clip.Height);
        var visible = Rectangle.Intersect(source, new Rectangle(Point.Empty, bitmap.Size));

        if (visible != source)
        {
            using var brush = new SolidBrush(Theme.BaseTop);
            g.FillRectangle(brush, clip);
        }
        if (visible.IsEmpty)
            return;

        var dest = new Rectangle(visible.X - origin.X, visible.Y - origin.Y, visible.Width, visible.Height);

        var compositing = g.CompositingMode;
        var interpolation = g.InterpolationMode;
        var pixelOffset = g.PixelOffsetMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bitmap, dest, visible, GraphicsUnit.Pixel);
        g.CompositingMode = compositing;
        g.InterpolationMode = interpolation;
        g.PixelOffsetMode = pixelOffset;
    }

    private void BuildBase(Size size)
    {
        _base?.Dispose();
        _blurred?.Dispose();

        _base = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_base))
        {
            var bounds = new Rectangle(Point.Empty, size);
            if (_source is null)
            {
                using var gradient = new LinearGradientBrush(bounds, Theme.BaseTop, Theme.BaseBottom, 60f);
                g.FillRectangle(gradient, bounds);
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var scale = Math.Max(size.Width / (float)_source.Width, size.Height / (float)_source.Height);
                var w = (int)Math.Ceiling(_source.Width * scale);
                var h = (int)Math.Ceiling(_source.Height * scale);
                g.DrawImage(_source, (size.Width - w) / 2, (size.Height - h) / 2, w, h);

                using var shade = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
                g.FillRectangle(shade, bounds);
            }
        }

        _blurred = Blur(_base);
        _baseSize = size;
    }

    private static GraphicsPath RectanglePath(Rectangle r)
    {
        var path = new GraphicsPath();
        path.AddRectangle(r);
        return path;
    }

    private static Bitmap Blur(Bitmap source)
    {
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);

        using var small = new Bitmap(Math.Max(1, source.Width / BlurFactor), Math.Max(1, source.Height / BlurFactor), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(source, new Rectangle(0, 0, small.Width, small.Height),
                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(small, new Rectangle(0, 0, result.Width, result.Height),
                        0, 0, small.Width, small.Height, GraphicsUnit.Pixel, attributes);
        }
        return result;
    }

    public void Dispose()
    {
        _source?.Dispose();
        _base?.Dispose();
        _blurred?.Dispose();
        _composed?.Dispose();
    }
}
