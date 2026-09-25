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

    // Окно появляется на экране только полностью нарисованным: до этого Windows держит его скрытым (DWM cloak).
    // Так не видно, как таблицы и поля дорисовываются кусками
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkChrome.TitleBar(this);
        DarkChrome.Cloak(this, true);
    }

    protected override void OnLoad(EventArgs e)
    {
        Theme.EnableDoubleBuffering(this);
        DarkChrome.ApplyTree(this);
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DarkChrome.PaintNow(this);
        DarkChrome.Cloak(this, false);
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
