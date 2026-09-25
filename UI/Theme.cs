using System.Drawing.Drawing2D;
using System.Reflection;
using PADLOck.Models;

namespace PADLOck.UI;

public enum ButtonKind { Primary, Secondary, Danger, Warning }

public static class Theme
{
    // Базовый фон, если картинка не выбрана
    public static readonly Color BaseTop = Color.FromArgb(15, 23, 42);
    public static readonly Color BaseBottom = Color.FromArgb(30, 41, 59);

    // Стекло
    public static readonly Color CardTint = Color.FromArgb(115, 15, 23, 42);
    public static readonly Color HeaderTint = Color.FromArgb(150, 2, 6, 23);
    public static readonly Color GlassBorder = Color.FromArgb(40, 255, 255, 255);

    // Элементы ввода (непрозрачные)
    public static readonly Color InputBack = Color.FromArgb(30, 41, 59);
    public static readonly Color InputBorder = Color.FromArgb(71, 85, 105);
    public static readonly Color LogBack = Color.FromArgb(15, 23, 42);
    public static readonly Color GridLines = Color.FromArgb(51, 65, 85);

    public static readonly Color TextColor = Color.FromArgb(241, 245, 249);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Dimmed = Color.FromArgb(100, 116, 139);
    public static readonly Color Accent = Color.FromArgb(59, 130, 246);
    public static readonly Color AccentText = Color.FromArgb(96, 165, 250);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color DangerText = Color.FromArgb(248, 113, 113);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color WarningText = Color.FromArgb(251, 191, 36);
    public static readonly Color SuccessText = Color.FromArgb(74, 222, 128);
    public static readonly Color Selection = Color.FromArgb(90, 59, 130, 246);
    public static readonly Color TrackBack = Color.FromArgb(40, 255, 255, 255);

    public static readonly Font UiFont = new("Segoe UI", 9.75f);
    public static readonly Font SmallFont = new("Segoe UI", 9f);
    public static readonly Font BoldFont = new("Segoe UI Semibold", 9.75f);
    public static readonly Font ChipFont = new("Segoe UI Semibold", 8.5f);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 12f);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 15f);
    public static readonly Font MonoFont = new("Consolas", 9f);

    public static string CategoryText(PackageCategory c) => c switch
    {
        PackageCategory.Remove => "Удалить",
        PackageCategory.Optional => "Спорный",
        PackageCategory.Keep => "Оставить",
        PackageCategory.Critical => "Критичный",
        _ => "Нет данных"
    };

    public static (Color Fore, Color Back) CategoryColors(PackageCategory c) => c switch
    {
        PackageCategory.Remove => (Color.FromArgb(134, 239, 172), Color.FromArgb(200, 20, 83, 45)),
        PackageCategory.Optional => (Color.FromArgb(253, 230, 138), Color.FromArgb(200, 120, 53, 15)),
        PackageCategory.Keep => (Color.FromArgb(147, 197, 253), Color.FromArgb(200, 30, 58, 138)),
        PackageCategory.Critical => (Color.FromArgb(252, 165, 165), Color.FromArgb(200, 127, 29, 29)),
        _ => (Color.FromArgb(203, 213, 225), Color.FromArgb(200, 51, 65, 85))
    };

    public static Button CreateButton(string text, ButtonKind kind)
    {
        var b = new Button
        {
            Text = text,
            Font = BoldFont,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Height = 34,
            Width = TextRenderer.MeasureText(text, BoldFont).Width + 36,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = false
        };
        SetKind(b, kind);
        return b;
    }

    public static void SetKind(Button b, ButtonKind kind)
    {
        var (back, border) = kind switch
        {
            ButtonKind.Primary => (Accent, Accent),
            ButtonKind.Danger => (Danger, Danger),
            ButtonKind.Warning => (Warning, Warning),
            _ => (Color.FromArgb(51, 65, 85), InputBorder)
        };
        b.BackColor = back;
        b.FlatAppearance.BorderColor = border;
        b.FlatAppearance.BorderSize = kind == ButtonKind.Secondary ? 1 : 0;
        b.FlatAppearance.MouseOverBackColor = Shade(back, 18);
        b.FlatAppearance.MouseDownBackColor = Shade(back, -18);
    }

    public static Label SectionLabel(string text) => new()
    {
        Text = text, Font = SectionFont, ForeColor = TextColor, BackColor = Color.Transparent,
        AutoSize = true, Margin = new Padding(0, 0, 10, 0)
    };

    public static Label MutedLabel(string text) => new()
    {
        Text = text, Font = UiFont, ForeColor = Muted, BackColor = Color.Transparent,
        AutoSize = true, Margin = new Padding(0, 4, 0, 0)
    };

    public static void StyleInput(Control c)
    {
        c.BackColor = InputBack;
        c.ForeColor = TextColor;
        c.Font = UiFont;
        if (c is TextBox t) t.BorderStyle = BorderStyle.FixedSingle;
        if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
    }

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
        row.Controls.AddRange(controls);
        return row;
    }

    public static TableLayoutPanel Stack(params (Control Control, bool Fill)[] items)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = items.Length,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < items.Length; i++)
        {
            t.RowStyles.Add(items[i].Fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            items[i].Control.Dock = DockStyle.Fill;
            t.Controls.Add(items[i].Control, 0, i);
        }
        return t;
    }

    public static GlassPanel Card(Backdrop backdrop, Control content)
    {
        var card = new GlassPanel(backdrop, CardTint, radius: 10, border: true)
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 14),
            Margin = new Padding(6)
        };
        card.Controls.Add(content);
        return card;
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BorderStyle = BorderStyle.None;
        g.BackgroundColor = BaseTop;
        g.GridColor = GridLines;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.MultiSelect = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 36;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.Transparent, ForeColor = Muted, Font = BoldFont,
            SelectionBackColor = Color.Transparent, SelectionForeColor = Muted,
            Padding = new Padding(4, 0, 0, 0)
        };
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.Transparent, ForeColor = TextColor, Font = UiFont,
            SelectionBackColor = Selection, SelectionForeColor = Color.White,
            Padding = new Padding(4, 0, 4, 0)
        };
        g.RowTemplate.Height = 34;
        g.DataError += (_, e) => e.ThrowException = false;

        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(g, true);
    }

    // У контейнеров WinForms двойная буферизация по умолчанию выключена — отсюда мигание
    public static void EnableDoubleBuffering(Control root)
    {
        var property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (Control child in root.Controls)
        {
            if (child is Panel or Label)
                property?.SetValue(child, true);
            EnableDoubleBuffering(child);
        }
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color color)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    public static void DrawLock(Graphics g, Rectangle cell, Color color)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = cell.X + cell.Width / 2f;
        float cy = cell.Y + cell.Height / 2f;
        using var pen = new Pen(color, 1.6f);
        g.DrawArc(pen, cx - 3.5f, cy - 7f, 7f, 7f, 180, 180);
        g.DrawLine(pen, cx - 3.5f, cy - 3.5f, cx - 3.5f, cy - 1f);
        g.DrawLine(pen, cx + 3.5f, cy - 3.5f, cx + 3.5f, cy - 1f);
        g.SmoothingMode = old;
        FillRounded(g, new Rectangle((int)(cx - 5.5f), (int)(cy - 1f), 11, 8), 2, color);
    }

    private static Color Shade(Color c, int delta) => Color.FromArgb(
        Math.Clamp(c.R + delta, 0, 255), Math.Clamp(c.G + delta, 0, 255), Math.Clamp(c.B + delta, 0, 255));
}
