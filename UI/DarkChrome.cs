using System.Runtime.InteropServices;

namespace PADLOck.UI;

// Тёмное оформление того, что рисует сама Windows: заголовок окна, полосы прокрутки, выпадающие списки,
// контекстные меню и подсказки. На старых версиях Windows вызовы просто ничего не делают.
internal static class DarkChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CLOAK = 13;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    // Недокументированные функции uxtheme: разрешают тёмные полосы прокрутки у стандартных элементов
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    private static bool Win10Dark => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    // Один раз при запуске программы
    public static void Initialize()
    {
        ToolStripManager.Renderer = new DarkMenuRenderer();
        if (!Win10Dark) return;
        try
        {
            SetPreferredAppMode(2); // всегда тёмная
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    // Тёмный заголовок окна (Windows 10 1809+), на Windows 11 — ещё и цвет заголовка и рамки под тему программы
    public static void TitleBar(Form form)
    {
        if (!Win10Dark || !form.IsHandleCreated) return;
        var on = 1;
        if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
            DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, 4);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var caption = ColorRef(Theme.BaseTop);
            var border = ColorRef(Color.FromArgb(51, 65, 85));
            var text = ColorRef(Theme.TextColor);
            DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, 4);
            DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref border, 4);
            DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, 4);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);

    // Нарисовать окно со всеми элементами сразу, не дожидаясь очереди сообщений
    public static void PaintNow(Control c)
    {
        const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;
        if (c.IsHandleCreated)
            RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
    }

    // Скрыть окно от экрана, пока оно рисуется. Окно при этом живёт и рисуется как обычно
    public static void Cloak(Form form, bool cloak)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;
        var value = cloak ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, DWMWA_CLOAK, ref value, 4);
    }

    // Тёмные полосы прокрутки и выпадающие списки у всех элементов, включая добавленные позже
    public static void ApplyTree(Control root)
    {
        Apply(root);
        foreach (Control child in root.Controls)
            ApplyTree(child);
        root.ControlAdded -= OnControlAdded;
        root.ControlAdded += OnControlAdded;
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null) ApplyTree(e.Control);
    }

    private static void Apply(Control c)
    {
        if (!Win10Dark) return;
        var theme = c switch
        {
            ComboBox => "DarkMode_CFD",
            ScrollBar or TextBoxBase or ListBox or DataGridView or TreeView or ListView
                or UpDownBase or ScrollableControl => "DarkMode_Explorer",
            _ => null
        };
        if (theme is null) return;

        void Set()
        {
            try
            {
                AllowDarkModeForWindow(c.Handle, true);
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
            }
            SetWindowTheme(c.Handle, theme, null);
        }

        if (c.IsHandleCreated) Set();
        else c.HandleCreated += (_, _) => Set();
    }

    // Тёмные всплывающие подсказки
    public static void Style(ToolTip tip)
    {
        tip.OwnerDraw = true;
        tip.BackColor = Theme.BaseTop;
        tip.ForeColor = Theme.TextColor;
        tip.Popup += (_, e) =>
        {
            var size = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl!), Theme.UiFont);
            e.ToolTipSize = new Size(size.Width + 20, size.Height + 12);
        };
        tip.Draw += (_, e) =>
        {
            using var back = new SolidBrush(Theme.BaseTop);
            using var border = new Pen(Theme.InputBorder);
            e.Graphics.FillRectangle(back, e.Bounds);
            e.Graphics.DrawRectangle(border, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.UiFont, e.Bounds, Theme.TextColor,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // Контекстные меню в цветах программы
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextColor : Theme.Dimmed;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.FillRounded(e.Graphics, r, 4, Theme.Accent);
            using var pen = new Pen(Color.White, 2f);
            var cx = r.X + r.Width / 2f;
            var cy = r.Y + r.Height / 2f;
            e.Graphics.DrawLines(pen, new[] { new PointF(cx - 4, cy), new PointF(cx - 1, cy + 3), new PointF(cx + 4, cy - 3) });
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Back = Theme.BaseTop;
        private static readonly Color Hover = Color.FromArgb(51, 65, 85);
        private static readonly Color Border = Color.FromArgb(51, 65, 85);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color ToolStripBorder => Border;
    }
}
