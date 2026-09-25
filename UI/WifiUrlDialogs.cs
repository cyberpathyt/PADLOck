using PADLOck.Services;

namespace PADLOck.UI;

// Кнопка «Изменить Wi-Fi»: только сеть, остальные настройки планшета не трогаются
public sealed class WifiDialog : DarkDialog
{
    private static readonly string[] Securities = { "wpa2", "wpa3", "open" };

    private readonly TextBox _ssid = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _security = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _show = Forms.Check("Показать пароль");
    private readonly CheckBox _forget = Forms.Check("Забыть остальные сохранённые сети");
    private readonly CheckBox _save = Forms.Check("Сохранить в настройках прошивки");

    public WifiDialog(KioskProfile profile, int deviceCount)
    {
        Forms.StyleDialog(this, "Изменить Wi-Fi", new Size(520, 400));

        _security.Items.AddRange(Securities);
        _ssid.Text = profile.WifiSsid;
        _password.Text = profile.WifiPassword;
        _security.SelectedItem = Securities.Contains(profile.WifiSecurity) ? profile.WifiSecurity : "wpa2";
        _forget.Checked = profile.WifiForgetOthers;
        _save.Checked = true;
        _show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_show.Checked;
        _security.SelectedIndexChanged += (_, _) => _password.Enabled = (string?)_security.SelectedItem != "open";

        var form = DialogLayout.Form(
            DialogLayout.Hint($"Отмеченных планшетов: {deviceCount}. Меняется только сеть Wi-Fi: планшет добавит её, " +
                              "подключится и забудет старые (если отмечено). Киоск, PIN и приложения не трогаются."),
            DialogLayout.Field("Сеть (SSID)", _ssid),
            DialogLayout.Field("Пароль", _password),
            DialogLayout.Field("", _show),
            DialogLayout.Field("Защита", _security),
            DialogLayout.Field("", _forget),
            DialogLayout.Field("", _save),
            DialogLayout.Hint("Если новая сеть сейчас вне зоны, планшет её запомнит и подключится, когда она появится. " +
                              "Чтобы не остаться без связи, старые сети в этом случае лучше не забывать."));

        var ok = Theme.CreateButton("Применить", ButtonKind.Primary);
        ok.Click += (_, _) =>
        {
            if (Ssid.Length == 0)
            {
                MessageBox.Show(this, "Укажите имя сети.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Security != "open" && Password.Length < 8)
            {
                MessageBox.Show(this, "Пароль сети WPA — не короче 8 символов.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.CreateButton("Отмена", ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;

        foreach (var input in new Control[] { _ssid, _password, _security })
            Theme.StyleInput(input);
        Controls.Add(form);
        Controls.Add(Forms.ButtonBar(ok, cancel));
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Ssid => _ssid.Text.Trim();
    public string Password => _password.Text;
    public string Security => (string?)_security.SelectedItem ?? "wpa2";
    public bool ForgetOthers => _forget.Checked;
    public bool SaveToProfile => _save.Checked;
}

// Кнопка «Заменить URL»: только адрес страницы киоска
public sealed class UrlDialog : DarkDialog
{
    private readonly TextBox _url = new() { PlaceholderText = "https://..." };
    private readonly CheckBox _save = Forms.Check("Сохранить в настройках прошивки");

    public UrlDialog(KioskProfile profile, int deviceCount)
    {
        Forms.StyleDialog(this, "Заменить URL", new Size(560, 270));
        _url.Text = profile.KioskUrl;
        _save.Checked = true;

        var modeNote = profile.KioskMode == "app"
            ? "\nСейчас в настройках киоск показывает приложение — после замены планшеты будут показывать сайт."
            : "";
        var form = DialogLayout.Form(
            DialogLayout.Hint($"Отмеченных планшетов: {deviceCount}. Меняется только адрес страницы, " +
                              "остальные настройки киоска остаются. Нужен PIN киоска из настроек прошивки." + modeNote),
            DialogLayout.Field("Адрес страницы", _url),
            DialogLayout.Field("", _save));

        var ok = Theme.CreateButton("Применить", ButtonKind.Primary);
        ok.Click += (_, _) =>
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                MessageBox.Show(this, "Адрес должен начинаться с http:// или https://", Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.CreateButton("Отмена", ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;

        Theme.StyleInput(_url);
        Controls.Add(form);
        Controls.Add(Forms.ButtonBar(ok, cancel));
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Url => _url.Text.Trim();
    public bool SaveToProfile => _save.Checked;
}

internal static class DialogLayout
{
    public static TableLayoutPanel Form(params Control[] rows)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            BackColor = Theme.BaseBottom,
            Padding = new Padding(20, 16, 20, 8)
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in rows)
        {
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.Dock = DockStyle.Fill;
            page.Controls.Add(row);
        }
        return page;
    }

    public static TableLayoutPanel Field(string caption, Control control)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 2, 0, 2), BackColor = Theme.BaseBottom };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        if (control is TextBox or ComboBox)
            control.Dock = DockStyle.Fill;
        row.Controls.Add(control, 1, 0);
        return row;
    }

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Dimmed,
        MaximumSize = new Size(500, 0),
        Margin = new Padding(0, 2, 0, 10)
    };
}
