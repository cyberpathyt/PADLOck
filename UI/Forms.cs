namespace PADLOck.UI;

// Базовый тёмный диалог.
// WS_EX_COMPOSITED здесь не используется: с ним выпадающие списки бесконечно перерисовываются (анимация наведения).
public class DarkDialog : Form
{
    public DarkDialog()
    {
        DoubleBuffered = true;
        Icon = Forms.AppIcon;
    }

    private const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);

    private System.Windows.Forms.Timer? _fade;

    // Окно показывается только после того, как полностью нарисовано, и плавно проявляется.
    // Без этого большие таблицы видно, как они дорисовываются сверху вниз
    protected override void OnLoad(EventArgs e)
    {
        Theme.EnableDoubleBuffering(this);
        Opacity = 0;
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        _fade = new System.Windows.Forms.Timer { Interval = 15 };
        _fade.Tick += (_, _) =>
        {
            var next = Opacity + 0.2;
            if (next >= 1)
            {
                Opacity = 1;
                _fade?.Stop();
                _fade?.Dispose();
                _fade = null;
            }
            else
            {
                Opacity = next;
            }
        };
        _fade.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _fade?.Dispose();
        base.Dispose(disposing);
    }
}

internal static class Forms
{
    private static Icon? _appIcon;

    public static Icon? AppIcon =>
        _appIcon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath);

    public static void StyleDialog(Form f, string title, Size size)
    {
        f.Text = title;
        f.Font = Theme.UiFont;
        f.ForeColor = Theme.TextColor;
        f.BackColor = Theme.BaseBottom;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MinimizeBox = false;
        f.MaximizeBox = false;
        f.ShowInTaskbar = false;
        f.ClientSize = size;
    }

    public static FlowLayoutPanel ButtonBar(params Button[] buttons)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 58,
            Padding = new Padding(12),
            BackColor = Theme.BaseTop
        };
        bar.Controls.AddRange(buttons);
        return bar;
    }

    public static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextColor,
        Margin = new Padding(0, 4, 0, 4)
    };

    public static NumericUpDown Number(int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        Width = 90,
        BackColor = Theme.InputBack,
        ForeColor = Theme.TextColor,
        BorderStyle = BorderStyle.FixedSingle
    };

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Dimmed,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 2, 0, 8)
    };
}
