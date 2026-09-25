namespace PADLOck.UI;

public class GlassPanel : Panel
{
    private readonly Backdrop _backdrop;

    public GlassPanel(Backdrop backdrop, Color tint, int radius, bool border)
    {
        _backdrop = backdrop;
        Tint = tint;
        Radius = radius;
        HasBorder = border;
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        backdrop.Register(this);
    }

    public Color Tint { get; }
    public int Radius { get; }
    public bool HasBorder { get; }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        _backdrop.Paint(e.Graphics, this, e.ClipRectangle);
}
