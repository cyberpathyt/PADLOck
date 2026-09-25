namespace PADLOck.UI;

// Журнал, нарисованный прямо поверх фона. Рисуются только видимые строки.
public sealed class GlassLog : Control
{
    private const int MaxLines = 5000;
    private static readonly Color Overlay = Color.FromArgb(70, 0, 0, 0);
    private static readonly Color LineColor = Color.FromArgb(203, 213, 225);

    private readonly Backdrop _backdrop;
    private readonly List<string> _lines = new();
    private readonly VScrollBar _scroll = new() { Dock = DockStyle.Right };
    private readonly int _lineHeight;

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
        _scroll.Scroll += (_, _) => Invalidate();
        Controls.Add(_scroll);
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
        var atEnd = _scroll.Value >= MaxScroll;
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

    private void UpdateScroll(bool toEnd)
    {
        _scroll.Minimum = 0;
        _scroll.Maximum = Math.Max(0, _lines.Count - 1);
        _scroll.LargeChange = VisibleLines;
        _scroll.SmallChange = 1;
        _scroll.Enabled = _lines.Count > VisibleLines;
        _scroll.Value = toEnd ? MaxScroll : Math.Min(_scroll.Value, MaxScroll);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll(_scroll.Value >= MaxScroll);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll.Value = Math.Clamp(_scroll.Value - Math.Sign(e.Delta) * 3, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        _backdrop.Paint(e.Graphics, this, e.ClipRectangle);
        using var overlay = new SolidBrush(Overlay);
        e.Graphics.FillRectangle(overlay, e.ClipRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var width = ClientSize.Width - _scroll.Width - 12;
        var first = _scroll.Value;
        var last = Math.Min(_lines.Count, first + VisibleLines + 1);
        for (int i = first; i < last; i++)
        {
            var line = _lines[i];
            var bounds = new Rectangle(6, 4 + (i - first) * _lineHeight, width, _lineHeight);
            TextRenderer.DrawText(e.Graphics, line, Font, bounds, ColorFor(line),
                TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
