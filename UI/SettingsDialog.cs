using PADLOck.Services;

namespace PADLOck.UI;

public sealed class SettingsDialog : DarkDialog
{
    private static readonly string[] TimeZones =
    {
        "Europe/Kaliningrad", "Europe/Moscow", "Europe/Samara", "Asia/Yekaterinburg", "Asia/Omsk",
        "Asia/Novosibirsk", "Asia/Krasnoyarsk", "Asia/Irkutsk", "Asia/Yakutsk", "Asia/Vladivostok",
        "Asia/Magadan", "Asia/Kamchatka"
    };
    private static readonly string[] Languages = { "ru-RU", "en-US" };
    private static readonly string[] Securities = { "wpa2", "wpa3", "open" };
    private static readonly (string Value, string Text)[] Modes = { ("web", "Сайт"), ("app", "Приложение") };
    private static readonly (string Value, string Text)[] BackModes =
    {
        ("immediate", "Сразу вернуть в киоск"),
        ("timer", "Вернуть через несколько секунд"),
        ("test", "Тестовый режим — не блокировать"),
    };

    private readonly KioskProfile _profile;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.BaseBottom, Padding = new Padding(20, 16, 20, 16) };
    private readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Left, Width = 190, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        BackColor = Theme.BaseTop, Padding = new Padding(10, 14, 10, 10)
    };
    private readonly List<(Button Button, Control Page)> _pages = new();

    // Киоск
    private readonly TextBox _apkFolder = new() { PlaceholderText = "по умолчанию — APK из релиза программы (приходит с обновлением)" };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _url = new() { PlaceholderText = "https://..." };
    private readonly TextBox _app = new() { PlaceholderText = "com.example.app" };
    private readonly TextBox _pin = new() { UseSystemPasswordChar = true, MaxLength = 6 };
    private readonly ComboBox _backMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _powerButton = Forms.Check("Разрешить кнопку питания");
    private readonly CheckBox _statusBar = Forms.Check("Показывать строку состояния");
    private readonly CheckBox _statusBattery = Forms.Check("батарея");
    private readonly CheckBox _statusWifi = Forms.Check("Wi-Fi");
    private readonly CheckBox _statusTime = Forms.Check("часы");
    private readonly CheckBox _screensaver = Forms.Check("Заставка при бездействии");
    private readonly NumericUpDown _screensaverDelay = Forms.Number(1, 240);
    private readonly NumericUpDown _screensaverBrightness = Forms.Number(0, 100);
    private readonly CheckBox _brightnessManaged = Forms.Check("Задать яркость экрана");
    private readonly NumericUpDown _brightness = Forms.Number(0, 100);

    // Wi-Fi
    private readonly TextBox _ssid = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _showPassword = Forms.Check("Показать пароль");
    private readonly ComboBox _security = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _forgetOthers = Forms.Check("Забывать остальные сохранённые сети");

    // Регион
    private readonly ComboBox _timeZone = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _ntp = new() { PlaceholderText = "не задан" };
    private readonly CheckBox _syncTime = Forms.Check("Выставлять время по этому компьютеру");

    // Система
    private readonly CheckBox _noLock = Forms.Check("Без экрана блокировки");
    private readonly CheckBox _noLocation = Forms.Check("Выключить геолокацию и фоновое сканирование");

    // Мониторинг
    private readonly CheckBox _mqtt = Forms.Check("Отправлять статус на MQTT-брокер");
    private readonly TextBox _broker = new() { PlaceholderText = "IP или имя сервера" };
    private readonly NumericUpDown _port = Forms.Number(1, 65535);
    private readonly TextBox _mqttUser = new();
    private readonly TextBox _mqttPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _baseTopic = new();
    private readonly NumericUpDown _interval = Forms.Number(5, 3600);
    private readonly CheckBox _allowControl = Forms.Check("Разрешить команды с пульта");

    // Приложения
    private readonly TextBox _packages = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 330 };
    private readonly CheckBox _reboot = Forms.Check("Перезагружать планшет после прошивки");

    public SettingsDialog(KioskProfile profile)
    {
        _profile = profile;
        Forms.StyleDialog(this, "Настройки прошивки", new Size(840, 640));

        _mode.Items.AddRange(Modes.Select(m => (object)m.Text).ToArray());
        _backMode.Items.AddRange(BackModes.Select(m => (object)m.Text).ToArray());
        _security.Items.AddRange(Securities);
        _timeZone.Items.AddRange(TimeZones);
        _language.Items.AddRange(Languages);

        AddPage("Киоск", BuildKioskPage());
        AddPage("Wi-Fi", Page(
            Row("Сеть (SSID)", _ssid),
            Row("Пароль", _password),
            Row("", _showPassword),
            Row("Защита", _security),
            Row("", _forgetOthers),
            Forms.Hint("Проверка доступа в интернет отключается: планшет остаётся в сети, даже если интернета в ней нет.")));
        AddPage("Регион и время", Page(
            Row("Часовой пояс", _timeZone),
            Row("Язык интерфейса", _language),
            Row("NTP-сервер", _ntp),
            Row("", _syncTime),
            Forms.Hint("Язык меняется у FreeKiosk и Настроек. В сети без интернета время не синхронизируется само — укажите внутренний NTP-сервер, если он есть.")));
        AddPage("Система", Page(Row("", _noLock), Row("", _noLocation)));
        AddPage("Мониторинг", Page(
            Row("", _mqtt),
            Row("Брокер", _broker),
            Row("Порт", _port),
            Row("Логин", _mqttUser),
            Row("Пароль", _mqttPassword),
            Row("Базовый топик", _baseTopic),
            Row("Интервал, сек", _interval),
            Row("", _allowControl),
            Forms.Hint("Имя устройства в MQTT — имя планшета из списка слева (двойной клик — переименовать). Если имя не задано, используется серийный номер.")));
        AddPage("Приложения", Page(
            Row("Удалять\n(по одному\nна строку)", _packages),
            Row("", _reboot),
            Forms.Hint("FreeKiosk, Настройки, лаунчер, клавиатура, WebView и системные компоненты не удаляются, даже если указаны в списке.")));

        var save = Theme.CreateButton("Сохранить", ButtonKind.Secondary);
        save.Click += (_, _) => Accept(apply: false);
        var saveApply = Theme.CreateButton("Сохранить и применить…", ButtonKind.Primary);
        saveApply.Click += (_, _) => Accept(apply: true);
        var cancel = Theme.CreateButton("Отмена", ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.Add(_content);
        Controls.Add(_nav);
        Controls.Add(Forms.ButtonBar(saveApply, save, cancel));
        CancelButton = cancel;

        foreach (var input in new Control[] { _apkFolder, _mode, _url, _app, _pin, _backMode, _ssid, _password, _security,
                                              _timeZone, _language, _ntp, _broker, _mqttUser, _mqttPassword, _baseTopic, _packages })
            Theme.StyleInput(input);

        _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
        _mode.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _statusBar.CheckedChanged += (_, _) => UpdateEnabled();
        _screensaver.CheckedChanged += (_, _) => UpdateEnabled();
        _brightnessManaged.CheckedChanged += (_, _) => UpdateEnabled();
        _mqtt.CheckedChanged += (_, _) => UpdateEnabled();

        LoadValues();
        ShowPage(0);
    }

    public bool ApplyRequested { get; private set; }

    private Control BuildKioskPage()
    {
        var browse = Theme.CreateButton("Обзор…", ButtonKind.Secondary);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Папка с APK FreeKiosk" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _apkFolder.Text = dialog.SelectedPath;
        };
        var folder = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.BaseBottom };
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _apkFolder.Dock = DockStyle.Fill;
        folder.Controls.Add(_apkFolder, 0, 0);
        folder.Controls.Add(browse, 1, 0);

        return Page(
            Row("Папка с APK", folder),
            Row("Что показывать", _mode),
            Row("Адрес страницы", _url),
            Row("Приложение", _app),
            Row("PIN (4–6 цифр)", _pin),
            Forms.Hint("Это PIN самого FreeKiosk, а не пароль экрана блокировки Android. Он нужен, чтобы выйти из киоска: " +
                       "5 нажатий в правом нижнем углу, затем PIN.\n" +
                       "• Новый планшет: этот PIN станет PIN киоска.\n" +
                       "• Уже настроенный планшет: FreeKiosk примет изменения, только если PIN совпадает с тем, что на нём стоит. " +
                       "Если FreeKiosk открывали вручную и PIN не меняли — там стоит 1234.\n" +
                       "Держите один PIN на всех планшетах, иначе одной кнопкой их не обновить."),
            Row("Кнопка «Назад»", _backMode),
            Row("", _powerButton),
            Row("", Inline(_statusBar, _statusBattery, _statusWifi, _statusTime)),
            Row("", Inline(_screensaver, Caption("через, мин"), _screensaverDelay, Caption("яркость, %"), _screensaverBrightness)),
            Row("", Inline(_brightnessManaged, _brightness)));
    }

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 6, 4, 0)
    };

    private static FlowLayoutPanel Inline(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.BaseBottom };
        foreach (var c in controls.Skip(1).OfType<CheckBox>())
            c.Margin = new Padding(12, 4, 0, 4);
        row.Controls.AddRange(controls);
        return row;
    }

    private static TableLayoutPanel Page(params Control[] rows)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            BackColor = Theme.BaseBottom,
            Visible = false
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

    private static TableLayoutPanel Row(string caption, Control control)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 2, 0, 2), BackColor = Theme.BaseBottom };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        if (control is TextBox { Multiline: true })
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        else if (control is TextBox or ComboBox)
            control.Dock = DockStyle.Fill;
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private void AddPage(string title, Control page)
    {
        var button = new Button
        {
            Text = title,
            Width = 168,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            ForeColor = Theme.TextColor,
            BackColor = Theme.BaseTop,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 4),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        var index = _pages.Count;
        button.Click += (_, _) => ShowPage(index);
        _nav.Controls.Add(button);
        _content.Controls.Add(page);
        _pages.Add((button, page));
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Page.Visible = i == index;
            _pages[i].Button.BackColor = i == index ? Theme.InputBack : Theme.BaseTop;
            _pages[i].Button.Font = i == index ? Theme.BoldFont : Theme.UiFont;
        }
    }

    private void UpdateEnabled()
    {
        var app = _mode.SelectedIndex == 1;
        _url.Enabled = !app;
        _app.Enabled = app;
        foreach (var c in new Control[] { _statusBattery, _statusWifi, _statusTime })
            c.Enabled = _statusBar.Checked;
        _screensaverDelay.Enabled = _screensaverBrightness.Enabled = _screensaver.Checked;
        _brightness.Enabled = _brightnessManaged.Checked;
        foreach (var c in new Control[] { _broker, _port, _mqttUser, _mqttPassword, _baseTopic, _interval, _allowControl })
            c.Enabled = _mqtt.Checked;
    }

    private void LoadValues()
    {
        var p = _profile;
        _apkFolder.Text = p.ApkFolder;
        _mode.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Value == p.KioskMode));
        _url.Text = p.KioskUrl;
        _app.Text = p.KioskApp;
        _pin.Text = p.KioskPin;
        _backMode.SelectedIndex = Math.Max(0, Array.FindIndex(BackModes, m => m.Value == p.BackButtonMode));
        _powerButton.Checked = p.AllowPowerButton;
        _statusBar.Checked = p.StatusBarEnabled;
        _statusBattery.Checked = p.StatusBarBattery;
        _statusWifi.Checked = p.StatusBarWifi;
        _statusTime.Checked = p.StatusBarTime;
        _screensaver.Checked = p.ScreensaverEnabled;
        _screensaverDelay.Value = Math.Clamp(p.ScreensaverDelayMinutes, 1, 240);
        _screensaverBrightness.Value = Math.Clamp(p.ScreensaverBrightness, 0, 100);
        _brightnessManaged.Checked = p.BrightnessManaged;
        _brightness.Value = Math.Clamp(p.DefaultBrightness, 0, 100);

        _ssid.Text = p.WifiSsid;
        _password.Text = p.WifiPassword;
        _security.SelectedItem = Securities.Contains(p.WifiSecurity) ? p.WifiSecurity : "wpa2";
        _forgetOthers.Checked = p.WifiForgetOthers;

        _timeZone.Text = p.TimeZone;
        _language.Text = p.Language;
        _ntp.Text = p.NtpServer;
        _syncTime.Checked = p.SyncTimeFromPc;

        _noLock.Checked = p.DisableLockScreen;
        _noLocation.Checked = p.DisableLocation;

        _mqtt.Checked = p.MqttEnabled;
        _broker.Text = p.MqttBroker;
        _port.Value = Math.Clamp(p.MqttPort, 1, 65535);
        _mqttUser.Text = p.MqttUsername;
        _mqttPassword.Text = p.MqttPassword;
        _baseTopic.Text = p.MqttBaseTopic;
        _interval.Value = Math.Clamp(p.MqttStatusInterval, 5, 3600);
        _allowControl.Checked = p.MqttAllowControl;

        _packages.Text = string.Join(Environment.NewLine, p.RemovePackages);
        _reboot.Checked = p.RebootWhenDone;
        UpdateEnabled();
    }

    private void Accept(bool apply)
    {
        var p = _profile.Clone();
        p.ApkFolder = _apkFolder.Text.Trim();
        p.KioskMode = Modes[Math.Max(0, _mode.SelectedIndex)].Value;
        p.KioskUrl = _url.Text.Trim();
        p.KioskApp = _app.Text.Trim();
        p.KioskPin = _pin.Text.Trim();
        p.BackButtonMode = BackModes[Math.Max(0, _backMode.SelectedIndex)].Value;
        p.AllowPowerButton = _powerButton.Checked;
        p.StatusBarEnabled = _statusBar.Checked;
        p.StatusBarBattery = _statusBattery.Checked;
        p.StatusBarWifi = _statusWifi.Checked;
        p.StatusBarTime = _statusTime.Checked;
        p.ScreensaverEnabled = _screensaver.Checked;
        p.ScreensaverDelayMinutes = (int)_screensaverDelay.Value;
        p.ScreensaverBrightness = (int)_screensaverBrightness.Value;
        p.BrightnessManaged = _brightnessManaged.Checked;
        p.DefaultBrightness = (int)_brightness.Value;

        p.WifiSsid = _ssid.Text.Trim();
        p.WifiPassword = _password.Text;
        p.WifiSecurity = _security.SelectedItem as string ?? "wpa2";
        p.WifiForgetOthers = _forgetOthers.Checked;

        p.TimeZone = _timeZone.Text.Trim();
        p.Language = _language.Text.Trim();
        p.NtpServer = _ntp.Text.Trim();
        p.SyncTimeFromPc = _syncTime.Checked;

        p.DisableLockScreen = _noLock.Checked;
        p.DisableLocation = _noLocation.Checked;

        p.MqttEnabled = _mqtt.Checked;
        p.MqttBroker = _broker.Text.Trim();
        p.MqttPort = (int)_port.Value;
        p.MqttUsername = _mqttUser.Text.Trim();
        p.MqttPassword = _mqttPassword.Text;
        p.MqttBaseTopic = _baseTopic.Text.Trim().Length > 0 ? _baseTopic.Text.Trim() : "freekiosk";
        p.MqttStatusInterval = (int)_interval.Value;
        p.MqttAllowControl = _allowControl.Checked;

        p.RemovePackages = _packages.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        p.RebootWhenDone = _reboot.Checked;

        var error = CheckProfile(p);
        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _profile.CopyFrom(p);
        ApplyRequested = apply;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? CheckProfile(KioskProfile p)
    {
        if (p.KioskMode == "web" && p.KioskUrl.Length > 0 && !Uri.TryCreate(p.KioskUrl, UriKind.Absolute, out _))
            return "Адрес страницы должен начинаться с http:// или https://";
        if ((p.KioskConfigured || p.MqttEnabled) && (p.KioskPin.Length is < 4 or > 6 || !p.KioskPin.All(char.IsDigit)))
            return "Для настройки киоска и мониторинга нужен PIN из 4–6 цифр.";
        if (p.MqttEnabled && p.MqttBroker.Length == 0)
            return "Укажите адрес MQTT-брокера или выключите мониторинг.";
        if (p.TimeZone.Length == 0 || p.Language.Length == 0)
            return "Укажите часовой пояс и язык.";
        if (p.WifiSsid.Length > 0 && p.WifiSecurity != "open" && p.WifiPassword.Length < 8)
            return "Пароль Wi-Fi для WPA2/WPA3 — не короче 8 символов.";
        return null;
    }
}
