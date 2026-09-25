using System.Drawing.Drawing2D;

namespace PADLOck.UI;

public sealed class DropZone : Control
{
    private bool _hover;

    public event Action<string[]>? FilesSelected;

    public DropZone()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        AllowDrop = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        var files = ApkFiles(e);
        e.Effect = files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        _hover = files.Length > 0;
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _hover = false;
        Invalidate();
        var files = ApkFiles(e);
        if (files.Length > 0)
            FilesSelected?.Invoke(files);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите APK",
            Filter = "Android-пакеты (*.apk)|*.apk",
            Multiselect = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            FilesSelected?.Invoke(dialog.FileNames);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);

        using (var path = Theme.Rounded(r, 8))
        {
            using var brush = new SolidBrush(_hover ? Color.FromArgb(50, Theme.Accent) : Color.FromArgb(14, 255, 255, 255));
            g.FillPath(brush, path);
            using var pen = new Pen(_hover ? Theme.AccentText : Theme.Dimmed, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        var text = _hover ? "Отпустите, чтобы установить" : "Перетащите APK сюда\nили нажмите, чтобы выбрать файл";
        TextRenderer.DrawText(g, text, Theme.UiFont, r, _hover ? Theme.AccentText : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private static string[] ApkFiles(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => f.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).ToArray()
            : Array.Empty<string>();
}
