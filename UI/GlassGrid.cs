namespace PADLOck.UI;

// Таблица с прозрачными ячейками поверх общего фона
public sealed class GlassGrid : DataGridView
{
    private readonly Backdrop _backdrop;

    public GlassGrid(Backdrop backdrop)
    {
        _backdrop = backdrop;
        DoubleBuffered = true;
    }

    protected override void PaintBackground(Graphics graphics, Rectangle clipBounds, Rectangle gridBounds) =>
        _backdrop.Paint(graphics, this, clipBounds);

    // При прокрутке DataGridView сдвигает уже нарисованное вместе с фоном
    protected override void OnScroll(ScrollEventArgs e)
    {
        base.OnScroll(e);
        Invalidate();
    }
}
