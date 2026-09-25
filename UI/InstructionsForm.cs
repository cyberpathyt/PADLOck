using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PADLOck.Services;

namespace PADLOck.UI;

// Встроенная вики: страницы — файлы wiki\*.md рядом с программой.
// Файл с тем же именем в %APPDATA%\PADLOck\wiki заменяет страницу, новый — добавляет свою.
public sealed class InstructionsForm : DarkDialog
{
    private sealed record Page(string File, string Title, string[] Lines, string Text);

    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static readonly Font TitleFont = new("Segoe UI Semibold", 17f);
    private static readonly Font HeadingFont = new("Segoe UI Semibold", 12.5f);
    private static readonly Font BodyFont = new("Segoe UI", 10.5f);
    private static readonly Font BodyBold = new("Segoe UI Semibold", 10.5f);
    private static readonly Font CodeFont = new("Consolas", 10f);

    private readonly List<Page> _pages = LoadPages();
    private readonly List<Page> _visible = new();
    private readonly TextBox _search = new() { PlaceholderText = "Поиск по инструкциям" };
    private readonly ListBox _nav = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 36,
        BackColor = Theme.BaseTop,
        ForeColor = Theme.TextColor,
        IntegralHeight = false
    };
    private readonly RichTextBox _view = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.BaseBottom,
        ForeColor = Theme.TextColor,
        Font = BodyFont,
        DetectUrls = true,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        WordWrap = true
    };

    public InstructionsForm()
    {
        Forms.StyleDialog(this, "Инструкции — PADLOck", new Size(1100, 760));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);

        Theme.StyleInput(_search);
        _search.Dock = DockStyle.Top;
        var searchBox = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 10, 10, 6), BackColor = Theme.BaseTop };
        searchBox.Controls.Add(_search);

        var folder = Theme.CreateButton("Папка для своих страниц", ButtonKind.Secondary);
        folder.Dock = DockStyle.Fill;
        folder.Margin = Padding.Empty;
        folder.Click += (_, _) => OpenUserFolder();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 8, 10, 10), BackColor = Theme.BaseTop };
        bottom.Controls.Add(folder);

        var left = new Panel { Dock = DockStyle.Left, Width = 290, BackColor = Theme.BaseTop, Padding = new Padding(0, 0, 0, 0) };
        left.Controls.Add(_nav);
        left.Controls.Add(bottom);
        left.Controls.Add(searchBox);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(34, 22, 18, 12), BackColor = Theme.BaseBottom };
        content.Controls.Add(_view);

        Controls.Add(content);
        Controls.Add(left);

        _nav.DrawItem += DrawNavItem;
        _nav.SelectedIndexChanged += (_, _) => ShowSelected();
        _search.TextChanged += (_, _) => ApplyFilter();
        _view.LinkClicked += (_, e) => OpenLink(e.LinkText);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
            if (e.KeyCode == Keys.Escape) Close();
        };

        ApplyFilter();
    }

    private static List<Page> LoadPages()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { Path.Combine(AppInfo.ProgramDir, "wiki"), UserWikiDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.md"))
                files[Path.GetFileName(file)] = file;
        }

        var pages = new List<Page>();
        foreach (var (name, path) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var lines = File.ReadAllLines(path);
                var title = lines.FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() ?? Path.GetFileNameWithoutExtension(name);
                pages.Add(new Page(name, title, lines, string.Join("\n", lines)));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (pages.Count == 0)
            pages.Add(new Page("", "Инструкции не найдены", new[]
            {
                "# Инструкции не найдены",
                "Папка **wiki** рядом с программой пуста или удалена. Переустановите PADLOck.",
            }, ""));
        return pages;
    }

    private static string UserWikiDir => Path.Combine(AppInfo.DataDir, "wiki");

    private void ApplyFilter()
    {
        var query = _search.Text.Trim();
        var current = _nav.SelectedIndex >= 0 && _nav.SelectedIndex < _visible.Count ? _visible[_nav.SelectedIndex] : null;

        _visible.Clear();
        _visible.AddRange(query.Length == 0
            ? _pages
            : _pages.Where(p => p.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        _nav.BeginUpdate();
        _nav.Items.Clear();
        foreach (var page in _visible)
            _nav.Items.Add(page.Title);
        _nav.EndUpdate();

        if (_visible.Count == 0)
        {
            Render(new[] { "# Ничего не найдено", $"По запросу «{query}» совпадений нет." }, "");
            return;
        }
        var index = current is null ? 0 : Math.Max(0, _visible.IndexOf(current));
        _nav.SelectedIndex = index;
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_nav.SelectedIndex < 0 || _nav.SelectedIndex >= _visible.Count) return;
        Render(_visible[_nav.SelectedIndex].Lines, _search.Text.Trim());
    }

    private void DrawNavItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.InputBack : Theme.BaseTop))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
            using (var accent = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12));
        var text = new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, _nav.Items[e.Index]?.ToString(), selected ? Theme.BoldFont : Theme.UiFont, text,
                              selected ? Color.White : Theme.Muted,
                              TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // Разметка: # заголовок страницы, ## раздел, - пункт, 1. шаг, > совет, ! внимание, --- черта,
    // **жирный**, `команда`. Пустая строка — новый абзац.
    private void Render(string[] lines, string highlight)
    {
        if (_view.IsHandleCreated)
            SendMessage(_view.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            _view.Clear();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("# "))
                    Block(line[2..], TitleFont, Color.White, 0, 0, after: "\n\n");
                else if (line.StartsWith("## "))
                    Block(line[3..], HeadingFont, Theme.AccentText, 0, 0, before: "\n", after: "\n");
                else if (line.StartsWith("- "))
                    Inline("•   " + line[2..], 18, 22);
                else if (Regex.Match(line, @"^(\d+)\.\s+(.*)$") is { Success: true } m)
                    Inline($"{m.Groups[1].Value}.  {m.Groups[2].Value}", 18, 24);
                else if (line.StartsWith("> "))
                    Inline("**Совет.** " + line[2..], 18, 0, Theme.AccentText);
                else if (line.StartsWith("! "))
                    Inline("**Внимание!** " + line[2..], 18, 0, Theme.WarningText);
                else if (line == "---")
                    Block(new string('─', 60), BodyFont, Theme.GridLines, 0, 0, after: "\n");
                else if (line.Length == 0)
                    Append("\n", BodyFont, Theme.TextColor);
                else
                    Inline(line, 0, 0);
            }

            if (highlight.Length > 0)
                Highlight(highlight);
            _view.Select(0, 0);
        }
        finally
        {
            if (_view.IsHandleCreated)
            {
                SendMessage(_view.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                _view.Invalidate();
            }
        }
    }

    private void Block(string text, Font font, Color color, int indent, int hanging, string before = "", string after = "\n")
    {
        if (before.Length > 0) Append(before, BodyFont, color);
        _view.SelectionIndent = indent;
        _view.SelectionHangingIndent = hanging;
        Append(text, font, color);
        Append(after, BodyFont, color);
    }

    // Строка с **жирным** и `командами`
    private void Inline(string text, int indent, int hanging, Color? color = null)
    {
        _view.SelectionStart = _view.TextLength;
        _view.SelectionIndent = indent;
        _view.SelectionHangingIndent = hanging;
        var baseColor = color ?? Theme.TextColor;
        foreach (Match part in Regex.Matches(text, @"(\*\*[^*]+\*\*|`[^`]+`|[^*`]+|[*`])"))
        {
            var value = part.Value;
            if (value.Length > 4 && value.StartsWith("**") && value.EndsWith("**"))
                Append(value[2..^2], BodyBold, color ?? Color.White);
            else if (value.Length > 2 && value.StartsWith('`') && value.EndsWith('`'))
                Append(value[1..^1], CodeFont, Theme.WarningText);
            else
                Append(value, BodyFont, baseColor);
        }
        Append("\n", BodyFont, baseColor);
    }

    private void Append(string text, Font font, Color color)
    {
        _view.SelectionStart = _view.TextLength;
        _view.SelectionLength = 0;
        _view.SelectionFont = font;
        _view.SelectionColor = color;
        _view.SelectionBackColor = Theme.BaseBottom;
        _view.SelectedText = text;
    }

    private void Highlight(string query)
    {
        var first = -1;
        var start = 0;
        while (start < _view.TextLength)
        {
            var found = _view.Find(query, start, RichTextBoxFinds.None);
            if (found < 0) break;
            if (first < 0) first = found;
            _view.SelectionBackColor = Theme.Warning;
            _view.SelectionColor = Color.White;
            start = found + query.Length;
        }
        if (first >= 0)
        {
            _view.Select(first, 0);
            _view.ScrollToCaret();
        }
    }

    private static void OpenLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private void OpenUserFolder()
    {
        try
        {
            Directory.CreateDirectory(UserWikiDir);
            var readme = Path.Combine(UserWikiDir, "_как-добавить-страницу.txt");
            if (!File.Exists(readme))
                File.WriteAllText(readme,
                    "Свои страницы инструкций: файлы .md в этой папке.\r\n" +
                    "Файл с тем же именем, что в папке wiki программы, заменяет её страницу (например 10-new-tablet.md).\r\n" +
                    "Порядок страниц — по имени файла, поэтому имя начинается с номера.\r\n\r\n" +
                    "Разметка:\r\n# Заголовок страницы\r\n## Раздел\r\n- пункт списка\r\n1. шаг\r\n> совет\r\n! внимание\r\n---  черта\r\n" +
                    "**жирный текст**, `команда`\r\n\r\nПосле правки закройте и снова откройте окно «Инструкции».\r\n");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UserWikiDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
