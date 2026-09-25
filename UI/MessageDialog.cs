using System.Drawing.Drawing2D;

namespace PADLOck.UI;

public enum MessageKind { None, Info, Success, Warning, Error, Question }

public sealed record ResultRow(string Name, string Status, bool Ok);

// Окна сообщений в оформлении программы вместо системного MessageBox.
// Вызов совпадает с MessageBox.Show, поэтому заменить его можно один в один
public static class Dialogs
{
    public static DialogResult Show(IWin32Window? owner, string text, string caption = "PADLOck",
                                    MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
                                    MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var dialog = new MessageDialog(caption, HeadlineFor(caption, icon), text, KindOf(icon), buttons, defaultButton, null);
        return dialog.ShowDialog(owner);
    }

    public static DialogResult Show(string text, string caption = "PADLOck",
                                    MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) =>
        Show(null, text, caption, buttons, icon);

    // Итог операции по планшетам: крупный заголовок и строка на каждый планшет
    public static void Result(IWin32Window? owner, string headline, string text, IReadOnlyList<ResultRow> rows)
    {
        var kind = rows.All(r => r.Ok) ? MessageKind.Success : MessageKind.Warning;
        using var dialog = new MessageDialog("PADLOck", headline, text, kind, MessageBoxButtons.OK, MessageBoxDefaultButton.Button1, rows);
        dialog.ShowDialog(owner);
    }

    private static MessageKind KindOf(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Information => MessageKind.Info,
        MessageBoxIcon.Warning => MessageKind.Warning,
        MessageBoxIcon.Error => MessageKind.Error,
        MessageBoxIcon.Question => MessageKind.Question,
        _ => MessageKind.None
    };

    // Название окна часто просто «PADLOck» — тогда заголовок по смыслу сообщения
    private static string HeadlineFor(string caption, MessageBoxIcon icon) =>
        caption != "PADLOck" ? caption : icon switch
        {
            MessageBoxIcon.Warning => "Внимание",
            MessageBoxIcon.Error => "Ошибка",
            MessageBoxIcon.Question => "Подтверждение",
            _ => "PADLOck"
        };
}

internal sealed class MessageDialog : DarkDialog
{
    private const int DialogWidth = 500;
    private const int Pad = 26;
    private const int IconSize = 44;

    private static readonly Font QuestionFont = new("Segoe UI Semibold", 18f);
    private static readonly Font ResultHeadlineFont = new("Segoe UI Semibold", 14f);

    private readonly MessageKind _kind;

    public MessageDialog(string caption, string headline, string text, MessageKind kind,
                         MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton, IReadOnlyList<ResultRow>? rows)
    {
        _kind = kind;
        var textLeft = kind == MessageKind.None ? Pad : Pad + IconSize + 18;
        var textWidth = DialogWidth - textLeft - Pad;

        var headlineFont = rows is null ? Theme.SectionFont : ResultHeadlineFont;
        var headlineHeight = TextRenderer.MeasureText(headline, headlineFont, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;
        var body = text.Trim();
        var bodyHeight = body.Length == 0 ? 0
            : TextRenderer.MeasureText(body, Theme.UiFont, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BaseBottom };
        content.Paint += PaintIcon;

        var y = Pad - 2;
        content.Controls.Add(new Label
        {
            Text = headline, Font = headlineFont, ForeColor = Color.White, AutoSize = false,
            Location = new Point(textLeft, y), Size = new Size(textWidth, headlineHeight)
        });
        y += headlineHeight + 6;

        if (bodyHeight > 0)
        {
            content.Controls.Add(new Label
            {
                Text = body, Font = Theme.UiFont, ForeColor = Theme.Muted, AutoSize = false, UseMnemonic = false,
                Location = new Point(textLeft, y), Size = new Size(textWidth, bodyHeight)
            });
            y += bodyHeight + 6;
        }

        if (rows is { Count: > 0 })
        {
            y += 8;
            var list = BuildRows(rows, textWidth);
            list.Location = new Point(textLeft, y);
            content.Controls.Add(list);
            y += list.Height;
        }

        y = Math.Max(y, Pad + IconSize) + Pad - 6;

        Forms.StyleDialog(this, caption, new Size(DialogWidth, y + 58));
        Controls.Add(content);
        var bar = Forms.ButtonBar(CreateButtons(buttons, defaultButton));
        Controls.Add(bar);
    }

    private Button[] CreateButtons(MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton)
    {
        var spec = buttons switch
        {
            MessageBoxButtons.OKCancel => new[] { ("ОК", DialogResult.OK), ("Отмена", DialogResult.Cancel) },
            MessageBoxButtons.YesNo => new[] { ("Да", DialogResult.Yes), ("Нет", DialogResult.No) },
            MessageBoxButtons.YesNoCancel => new[] { ("Да", DialogResult.Yes), ("Нет", DialogResult.No), ("Отмена", DialogResult.Cancel) },
            MessageBoxButtons.RetryCancel => new[] { ("Повторить", DialogResult.Retry), ("Отмена", DialogResult.Cancel) },
            _ => new[] { ("ОК", DialogResult.OK) }
        };
        var index = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0
        };
        index = Math.Min(index, spec.Length - 1);

        var result = new List<Button>();
        for (var i = 0; i < spec.Length; i++)
        {
            var (text, value) = spec[i];
            var b = Theme.CreateButton(text, i == index ? ButtonKind.Primary : ButtonKind.Secondary);
            b.MinimumSize = new Size(96, 34);
            b.DialogResult = value;
            result.Add(b);
            if (i == index) AcceptButton = b;
            if (value is DialogResult.Cancel or DialogResult.No || spec.Length == 1) CancelButton = b;
        }
        // Панель кнопок раскладывает справа налево
        result.Reverse();
        return result.ToArray();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        (AcceptButton as Control)?.Focus();
    }

    private static Control BuildRows(IReadOnlyList<ResultRow> rows, int width)
    {
        const int rowHeight = 34;
        var panel = new Panel
        {
            Width = width,
            Height = Math.Min(rows.Count, 8) * rowHeight + 2,
            AutoScroll = rows.Count > 8,
            BackColor = Theme.BaseBottom
        };
        var inner = new Panel { Width = width - (rows.Count > 8 ? SystemInformation.VerticalScrollBarWidth : 0), Height = rows.Count * rowHeight, BackColor = Theme.BaseBottom };
        inner.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (var i = 0; i < rows.Count; i++)
            {
                var r = new Rectangle(0, i * rowHeight, inner.Width, rowHeight - 4);
                Theme.FillRounded(g, r, 8, Theme.BaseTop);
                var row = rows[i];
                var color = row.Ok ? Theme.SuccessText : Theme.DangerText;
                using (var dot = new SolidBrush(color))
                    g.FillEllipse(dot, r.X + 12, r.Y + r.Height / 2 - 4, 8, 8);
                var statusWidth = TextRenderer.MeasureText(row.Status, Theme.BoldFont).Width + 4;
                TextRenderer.DrawText(g, row.Name, Theme.BoldFont, new Rectangle(r.X + 28, r.Y, r.Width - statusWidth - 44, r.Height),
                                      Theme.TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, row.Status, Theme.BoldFont, new Rectangle(r.Right - statusWidth - 12, r.Y, statusWidth, r.Height),
                                      color, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        };
        panel.Controls.Add(inner);
        return panel;
    }

    private void PaintIcon(object? sender, PaintEventArgs e)
    {
        if (_kind == MessageKind.None) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _kind switch
        {
            MessageKind.Success => Color.FromArgb(34, 197, 94),
            MessageKind.Warning => Theme.Warning,
            MessageKind.Error => Theme.Danger,
            _ => Theme.Accent
        };
        var r = new Rectangle(Pad, Pad, IconSize, IconSize);
        using (var halo = new SolidBrush(Color.FromArgb(50, color)))
            g.FillEllipse(halo, Rectangle.Inflate(r, 5, 5));
        using (var fill = new SolidBrush(color))
            g.FillEllipse(fill, r);

        using var pen = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        switch (_kind)
        {
            case MessageKind.Success:
                g.DrawLines(pen, new[] { new PointF(cx - 9, cy + 1), new PointF(cx - 3, cy + 7), new PointF(cx + 10, cy - 7) });
                break;
            case MessageKind.Error:
                g.DrawLine(pen, cx - 7, cy - 7, cx + 7, cy + 7);
                g.DrawLine(pen, cx + 7, cy - 7, cx - 7, cy + 7);
                break;
            case MessageKind.Warning:
                g.DrawLine(pen, cx, cy - 10, cx, cy + 3);
                using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, cx - 2.2f, cy + 7, 4.4f, 4.4f);
                break;
            case MessageKind.Info:
                using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, cx - 2.2f, cy - 11, 4.4f, 4.4f);
                g.DrawLine(pen, cx, cy - 2, cx, cy + 10);
                break;
            case MessageKind.Question:
                TextRenderer.DrawText(g, "?", QuestionFont, r, Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                break;
        }
    }
}
