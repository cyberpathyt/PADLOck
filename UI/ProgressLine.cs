namespace PADLOck.UI;

// Тонкая полоса прогресса в цветах программы
public sealed class ProgressLine : Control
{
    private double _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.BaseBottom;
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 2, Width - 1, 10);
        Theme.FillRounded(e.Graphics, r, 5, Theme.TrackBack);
        var width = (int)(r.Width * _value);
        if (width >= 10)
            Theme.FillRounded(e.Graphics, new Rectangle(r.X, r.Y, width, r.Height), 5, Theme.Accent);
    }
}
