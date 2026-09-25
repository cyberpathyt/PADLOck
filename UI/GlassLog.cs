namespace PADLOck.UI;

// Журнал, нарисованный прямо поверх фона. Рисуются только видимые строки.
public sealed class GlassLog : Control
{
    private const int MaxLines = 5000;
    private static readonly Color Overlay = Color.FromArgb(70, 0, 0, 0);
    private static readonly Color LineColor = Color.FromArgb(203, 213, 225);

    private readonly Backdrop _backdrop;
    private readonly List<string> _lines = new();
    private const int ScrollWidth = 10;
    private readonly int _lineHeight;
    private int _top;              // первая видимая строка
    private int? _dragOffset;      // захват ползунка мышью
    private bool _hoverScroll;

    public GlassLog(Backdrop backdrop)
    {
        _backdrop = backdrop;
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);
        Font = Theme.MonoFont;
        _lineHeight = TextRenderer.MeasureText("Wg", Font).Height + 1;

    }

    public string AllText => string.Join(Environment.NewLine, _lines);

    public void SetLines(IEnumerable<string> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        Trim();
        UpdateScroll(toEnd: true);
        Invalidate();
    }

    public void AppendLine(string line)
    {
        var atEnd = _top >= MaxScroll;
        _lines.Add(line);
        Trim();
        UpdateScroll(atEnd);
        Invalidate();
    }

    private int VisibleLines => Math.Max(1, (ClientSize.Height - 8) / _lineHeight);
    private int MaxScroll => Math.Max(0, _lines.Count - VisibleLines);

    private void Trim()
    {
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
    }

    private void UpdateScroll(bool toEnd) =>
        _top = toEnd ? MaxScroll : Math.Clamp(_top, 0, MaxScroll);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll(_top >= MaxScroll);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_top - Math.Sign(e.Delta) * 3);
    }

    private void ScrollTo(int top)
    {
        var value = Math.Clamp(top, 0, MaxScroll);
        if (value == _top) return;
        _top = value;
        Invalidate();
    }

    // Своя тонкая полоса прокрутки в цветах программы вместо системной белой
    private Rectangle Track => new(ClientSize.Width - ScrollWidth - 3, 4, ScrollWidth, Math.Max(0, ClientSize.Height - 8));

    private Rectangle Thumb
    {
        get
        {
            var track = Track;
            if (_lines.Count <= VisibleLines || track.Height <= 0) return Rectangle.Empty;
            var height = Math.Max(28, (int)(track.Height * (VisibleLines / (double)_lines.Count)));
            var y = track.Y + (int)((track.Height - height) * (_top / (double)Math.Max(1, MaxScroll)));
            return new Rectangle(track.X, y, track.Width, height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || !Track.Contains(e.Location)) return;
        var thumb = Thumb;
        if (thumb.IsEmpty) return;
        if (thumb.Contains(e.Location))
            _dragOffset = e.Y - thumb.Y;
        else
            ScrollTo(_top + (e.Y < thumb.Y ? -VisibleLines : VisibleLines));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = Track.Contains(e.Location);
        if (hover != _hoverScroll)
        {
            _hoverScroll = hover;
            Invalidate(Track);
        }
        if (_dragOffset is not { } offset) return;
        var track = Track;
        var thumb = Thumb;
        var free = Math.Max(1, track.Height - thumb.Height);
        ScrollTo((int)Math.Round((e.Y - offset - track.Y) / (double)free * MaxScroll));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragOffset = null;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverScroll && _dragOffset is null)
        {
            _hoverScroll = false;
            Invalidate(Track);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        _backdrop.Paint(e.Graphics, this, e.ClipRectangle);
        using var overlay = new SolidBrush(Overlay);
        e.Graphics.FillRectangle(overlay, e.ClipRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var width = ClientSize.Width - ScrollWidth - 16;
        var first = _top;
        var last = Math.Min(_lines.Count, first + VisibleLines + 1);
        for (int i = first; i < last; i++)
        {
            var line = _lines[i];
            var bounds = new Rectangle(6, 4 + (i - first) * _lineHeight, width, _lineHeight);
            TextRenderer.DrawText(e.Graphics, line, Font, bounds, ColorFor(line),
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        var thumb = Thumb;
        if (!thumb.IsEmpty)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var active = _hoverScroll || _dragOffset is not null;
            if (active)
                Theme.FillRounded(e.Graphics, Track, ScrollWidth / 2, Color.FromArgb(30, 255, 255, 255));
            var bar = active ? thumb : new Rectangle(thumb.X + 3, thumb.Y, thumb.Width - 6, thumb.Height);
            Theme.FillRounded(e.Graphics, bar, bar.Width / 2, active ? Color.FromArgb(150, 148, 163, 184) : Color.FromArgb(90, 148, 163, 184));
        }
    }

    private static Color ColorFor(string line)
    {
        if (line.Contains(" ERR ") || line.Contains("ошибка", StringComparison.OrdinalIgnoreCase)
            || line.Contains("не выполнено") || line.Contains("не совпадает"))
            return Theme.DangerText;
        if (line.Contains(" OK ") || line.Contains("готово", StringComparison.OrdinalIgnoreCase))
            return Theme.SuccessText;
        if (line.Contains("> adb"))
            return Theme.Muted;
        return LineColor;
    }
}
