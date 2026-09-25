using System.Diagnostics;
using System.Runtime.InteropServices;
using PADLOck.Models;
using PADLOck.Services;
using PADLOck.UI;

namespace PADLOck;

public sealed class MainForm : Form
{
    private const string SystemSource = "Система";
    private const int MaxLogEntries = 5000;
    private const int MaxParallelInstalls = 5;
    private const int WM_SETREDRAW = 0x000B;

    private static readonly (string Text, PackageCategory? Category)[] CategoryFilters =
    {
        ("Все категории", null),
        ("Рекомендуется удалить", PackageCategory.Remove),
        ("Спорные", PackageCategory.Optional),
        ("Оставить", PackageCategory.Keep),
        ("Критичные", PackageCategory.Critical),
        ("Нет данных", PackageCategory.Unknown),
    };

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Состояние
    private readonly List<Device> _devices = new();
    private List<PackageRow> _packages = new();
    private PackageCatalog _catalog = PackageCatalog.Empty;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly KioskProfile _profile = KioskProfile.Load();
    private readonly FileLog _fileLog = new();
    private Device? _reference;
    private int _packageLoadVersion;
    private bool _polling;
    private bool _running;
    private bool _suppressGridEvents;
    private CancellationTokenSource? _stop;

    private readonly List<(DateTime Time, string Source, string Text)> _logEntries = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 2000 };
    private readonly SemaphoreSlim _installSlots = new(MaxParallelInstalls);
    private readonly Backdrop _backdrop;
    private readonly ToolTip _tips = new();

    // Шапка
    private readonly Button _instructionsButton = Theme.CreateButton("Инструкции", ButtonKind.Secondary);
    private readonly Button _updatesButton = Theme.CreateButton("Обновления", ButtonKind.Secondary);
    private readonly UpdateService _updateService = new();
    private bool _updating;
    private readonly Button _settingsButton = Theme.CreateButton("Настройки прошивки", ButtonKind.Primary);
    private readonly Button _advancedButton = Theme.CreateButton("Расширенный режим", ButtonKind.Secondary);
    private readonly Button _profilesButton = Theme.CreateButton("Базы пакетов", ButtonKind.Secondary);

    // Прошивка
    private readonly Label _provisionInfo = Theme.MutedLabel("");
    private readonly Button _provisionButton = Theme.CreateButton("Прошить", ButtonKind.Primary);
    private readonly Button _checkButton = Theme.CreateButton("Проверка", ButtonKind.Secondary);
    private readonly Button _wifiButton = Theme.CreateButton("Изменить Wi-Fi", ButtonKind.Secondary);
    private readonly Button _urlButton = Theme.CreateButton("Заменить URL", ButtonKind.Secondary);
    private InstructionsForm? _instructions;

    // Планшеты
    private readonly GlassGrid _devicesGrid;
    private readonly Label _devicesCount = Theme.MutedLabel("");
    private readonly Button _selectAllDevices = Theme.CreateButton("Выбрать все", ButtonKind.Secondary);
    private readonly Button _selectNoDevices = Theme.CreateButton("Снять", ButtonKind.Secondary);
    private readonly Button _resetButton = Theme.CreateButton("Сброс", ButtonKind.Danger);
    private readonly DropZone _dropZone = new();

    // Пакеты (расширенный режим)
    private readonly GlassGrid _pkgGrid;
    private readonly Label _referenceLabel = Theme.MutedLabel("подключите планшет");
    private readonly TextBox _search = new() { PlaceholderText = "Поиск по имени или описанию", Width = 250, Margin = new Padding(0, 4, 0, 0) };
    private readonly ComboBox _categoryFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(8, 4, 0, 0) };
    private readonly Label _markLabel = Theme.MutedLabel("Отметить:");
    private readonly Button _markRecommended = Theme.CreateButton("Рекомендуемые", ButtonKind.Secondary);
    private readonly Button _clearMarks = Theme.CreateButton("Снять", ButtonKind.Secondary);
    private readonly Label _selectionLabel = Theme.MutedLabel("");
    private readonly Button _restoreButton = Theme.CreateButton("Восстановить", ButtonKind.Secondary);
    private readonly Button _disableButton = Theme.CreateButton("Отключить", ButtonKind.Warning);
    private readonly Button _removeButton = Theme.CreateButton("Удалить", ButtonKind.Danger);

    // Журнал
    private readonly ComboBox _logFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 2, 0, 0) };
    private readonly Button _openLogsButton = Theme.CreateButton("Папка логов", ButtonKind.Secondary);
    private readonly GlassLog _log;

    private TableLayoutPanel _body = null!;
    private Control _packagesCard = null!;

    public MainForm()
    {
        _backdrop = new Backdrop(this);
        _devicesGrid = new GlassGrid(_backdrop);
        _pkgGrid = new GlassGrid(_backdrop);
        _log = new GlassLog(_backdrop);

        Text = $"PADLOck {AppInfo.Version}";
        Icon = Forms.AppIcon;
        Font = Theme.UiFont;
        ForeColor = Theme.TextColor;
        BackColor = Theme.BaseTop;
        MinimumSize = new Size(1180, 760);
        DoubleBuffered = true;
        RestoreWindow();

        SuspendLayout();
        BuildLayout();
        BuildMenus();
        WireEvents();
        ResumeLayout(true);
        Theme.EnableDoubleBuffering(this);
        DarkChrome.ApplyTree(this);
        DarkChrome.Style(_tips);

        Log(SystemSource, $"PADLOck {AppInfo.Version} · {AppInfo.Author} · данные: {AppInfo.DataDir}");
        LoadCatalog();
        UpdateProvisionInfo();
        ApplyMode();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        _backdrop.Paint(e.Graphics, this, e.ClipRectangle);

    // Главное окно тоже показывается только полностью нарисованным
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkChrome.TitleBar(this);
        DarkChrome.Cloak(this, true);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _backdrop.Rebuild();
        DarkChrome.PaintNow(this);
        DarkChrome.Cloak(this, false);

        // Проверка обновлений после запуска, когда окно уже на экране
        await Task.Delay(1500);
        await CheckUpdatesAsync(manual: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _backdrop.Dispose();
            _fileLog.Dispose();
            _pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Window

    private void RestoreWindow()
    {
        var s = _settings;
        var bounds = new Rectangle(s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight);
        var visible = s.WindowWidth >= MinimumSize.Width && s.WindowHeight >= MinimumSize.Height
                      && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds));
        if (visible)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1440, 920);
        }
        if (s.WindowMaximized)
            WindowState = FormWindowState.Maximized;
    }

    private void SaveWindow()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Не удалось сохранить настройки программы: " + ex.Message);
        }
    }

    // Перестройка раскладки без промежуточных кадров
    private void WithoutRedraw(Control control, Action action)
    {
        if (control.IsHandleCreated)
            SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            action();
        }
        finally
        {
            if (control.IsHandleCreated)
            {
                SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                _backdrop.Rebuild();
                control.Invalidate(true);
            }
        }
    }

    #endregion

    #region Layout

    private void BuildLayout()
    {
        var header = new GlassPanel(_backdrop, Theme.HeaderTint, radius: 0, border: false) { Dock = DockStyle.Top, Height = 66 };
        var title = new Label { Text = "PADLOck", Font = Theme.TitleFont, ForeColor = Color.White, BackColor = Color.Transparent, AutoSize = true, Location = new Point(18, 7) };
        var titleWidth = TextRenderer.MeasureText(title.Text, Theme.TitleFont).Width;
        var author = new Label
        {
            Text = $"v{AppInfo.Version} · by {AppInfo.Author}",
            Font = Theme.SmallFont,
            ForeColor = Theme.Dimmed,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(18 + titleWidth + 6, 16)
        };
        var subtitle = new Label { Text = "Подготовка Android-планшетов для режима киоска", Font = Theme.UiFont, ForeColor = Theme.Muted, BackColor = Color.Transparent, AutoSize = true, Location = new Point(20, 39) };
        header.Controls.Add(title);
        header.Controls.Add(author);
        header.Controls.Add(subtitle);

        var headerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 16, 16, 0)
        };
        headerButtons.Controls.Add(_updatesButton);
        headerButtons.Controls.Add(_instructionsButton);
        headerButtons.Controls.Add(_settingsButton);
        headerButtons.Controls.Add(_advancedButton);
        headerButtons.Controls.Add(_profilesButton);
        header.Controls.Add(headerButtons);

        _body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _body.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        _body.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        left.Controls.Add(BuildProvisionCard(), 0, 0);
        left.Controls.Add(BuildDevicesCard(), 0, 1);
        left.Controls.Add(BuildApkCard(), 0, 2);

        _body.Controls.Add(left, 0, 0);
        _body.SetRowSpan(left, 2);
        _packagesCard = BuildPackagesCard();
        _body.Controls.Add(_packagesCard, 1, 0);
        _body.Controls.Add(BuildLogCard(), 1, 1);

        Controls.Add(_body);
        Controls.Add(header);
    }

    private Control Card(Control content) => Theme.Card(_backdrop, content);

    private Control BuildProvisionCard()
    {
        var header = Theme.Row(Theme.SectionLabel("Прошивка под киоск"));
        header.Margin = new Padding(0, 0, 0, 4);
        _provisionInfo.Margin = new Padding(0, 0, 0, 10);
        _provisionButton.Margin = Padding.Empty;
        var buttons = Theme.Row(_provisionButton, _checkButton);
        buttons.Margin = Padding.Empty;
        _wifiButton.Margin = new Padding(0, 8, 0, 0);
        _urlButton.Margin = new Padding(8, 8, 0, 0);
        var quick = Theme.Row(_wifiButton, _urlButton);
        quick.Margin = Padding.Empty;
        _tips.SetToolTip(_provisionButton, "Прошивка отмеченных планшетов: полностью или выбранные шаги");
        _tips.SetToolTip(_checkButton, "Проверить, что планшеты настроены правильно");
        _tips.SetToolTip(_wifiButton, "Сменить только сеть Wi-Fi на отмеченных планшетах");
        _tips.SetToolTip(_urlButton, "Сменить только адрес страницы киоска на отмеченных планшетах");
        _tips.SetToolTip(_updatesButton, "Проверка обновлений программы и APK FreeKiosk, канал stable или dev");
        _tips.SetToolTip(_instructionsButton, "Как подготовить новый планшет, прошить его и что делать при ошибках");

        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in new Control[] { header, _provisionInfo, buttons, quick })
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(c);
        }
        return Card(stack);
    }

    private Control BuildDevicesCard()
    {
        var header = Theme.Row(Theme.SectionLabel("Планшеты"), _devicesCount);
        var hint = Theme.MutedLabel("Двойной клик — переименовать планшет.");
        hint.Margin = new Padding(0, 0, 0, 8);

        Theme.StyleGrid(_devicesGrid);
        _devicesGrid.ColumnHeadersVisible = false;
        _devicesGrid.RowTemplate.Height = 52;
        _devicesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", Width = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", ReadOnly = true, FillWeight = 55 });
        _devicesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", ReadOnly = true, FillWeight = 45 });

        _selectAllDevices.Margin = new Padding(0, 10, 0, 0);
        _selectNoDevices.Margin = new Padding(8, 10, 0, 0);
        _resetButton.Margin = new Padding(8, 10, 0, 0);
        var buttons = Theme.Row(_selectAllDevices, _selectNoDevices, _resetButton);
        buttons.Margin = Padding.Empty;
        _tips.SetToolTip(_resetButton, "Сброс отмеченных планшетов к заводским настройкам");

        return Card(Theme.Stack((header, false), (hint, false), (_devicesGrid, true), (buttons, false)));
    }

    private Control BuildApkCard()
    {
        var header = Theme.Row(Theme.SectionLabel("Установка APK"));
        header.Margin = new Padding(0, 0, 0, 4);
        var hint = Theme.MutedLabel("Ставится на все отмеченные планшеты");
        hint.Margin = new Padding(0, 0, 0, 8);
        return Card(Theme.Stack((header, false), (hint, false), (_dropZone, true)));
    }

    private Control BuildPackagesCard()
    {
        var header = Theme.Row(Theme.SectionLabel("Пакеты"), _referenceLabel);

        _categoryFilter.Items.AddRange(CategoryFilters.Select(c => (object)c.Text).ToArray());
        _categoryFilter.SelectedIndex = 0;
        Theme.StyleInput(_search);
        Theme.StyleInput(_categoryFilter);
        _markLabel.Margin = new Padding(16, 8, 0, 0);
        var toolbar = Theme.Row(_search, _categoryFilter, _markLabel, _markRecommended, _clearMarks);
        toolbar.WrapContents = true;

        Theme.StyleGrid(_pkgGrid);
        _pkgGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Check", HeaderText = "", Width = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _pkgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Название", ReadOnly = true, FillWeight = 20 });
        _pkgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Package", HeaderText = "Пакет", ReadOnly = true, FillWeight = 27 });
        _pkgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Категория", ReadOnly = true, FillWeight = 12 });
        _pkgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Описание", ReadOnly = true, FillWeight = 38 });
        _pkgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Статус", ReadOnly = true, FillWeight = 15 });

        var actions = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selectionLabel.Anchor = AnchorStyles.Left;
        var actionButtons = Theme.Row(_restoreButton, _disableButton, _removeButton);
        actionButtons.Margin = Padding.Empty;
        actions.Controls.Add(_selectionLabel, 0, 0);
        actions.Controls.Add(actionButtons, 1, 0);

        return Card(Theme.Stack((header, false), (toolbar, false), (_pkgGrid, true), (actions, false)));
    }

    private Control BuildLogCard()
    {
        _logFilter.Items.Add("Все устройства");
        _logFilter.SelectedIndex = 0;
        Theme.StyleInput(_logFilter);
        _openLogsButton.Height = 28;
        _openLogsButton.Margin = new Padding(8, 0, 0, 0);
        var header = Theme.Row(Theme.SectionLabel("Журнал"), _logFilter, _openLogsButton);
        return Card(Theme.Stack((header, false), (_log, true)));
    }

    private void BuildMenus()
    {
        var profiles = new ContextMenuStrip();
        profiles.Items.Add("Открыть папку своих баз", null, (_, _) => OpenFolder(AppInfo.UserProfilesDir));
        profiles.Items.Add("Перечитать базы", null, async (_, _) =>
        {
            LoadCatalog();
            await LoadPackagesAsync();
        });
        profiles.Items.Add(new ToolStripSeparator());
        profiles.Items.Add("Сохранить пакеты без описания…", null, (_, _) => ExportUnknownPackages());
        _profilesButton.Click += (_, _) => profiles.Show(_profilesButton, new Point(0, _profilesButton.Height));

        var updates = new ContextMenuStrip();
        updates.Items.Add("Проверить обновления", null, async (_, _) => await CheckUpdatesAsync(manual: true));
        var devChannel = new ToolStripMenuItem("Получать тестовые версии (dev)") { CheckOnClick = true, Checked = _settings.UpdateDevChannel };
        devChannel.CheckedChanged += (_, _) =>
        {
            _settings.UpdateDevChannel = devChannel.Checked;
            SaveSettings();
            Log(SystemSource, devChannel.Checked ? "Обновления: канал dev (тестовые версии)" : "Обновления: канал stable");
        };
        updates.Items.Add(devChannel);
        updates.Items.Add(new ToolStripSeparator());
        updates.Items.Add("Страница релизов на GitHub", null, (_, _) => OpenUrl($"https://github.com/{AppInfo.UpdateRepository}/releases"));
        _updatesButton.Click += (_, _) => updates.Show(_updatesButton, new Point(0, _updatesButton.Height));

        var deviceMenu = new ContextMenuStrip();
        deviceMenu.Items.Add("Переименовать…", null, async (_, _) =>
        {
            if (SelectedDevice() is { } d) await RenameDeviceAsync(d);
        });
        deviceMenu.Items.Add(new ToolStripSeparator());
        deviceMenu.Items.Add("Сброс к заводским…", null, async (_, _) =>
        {
            if (SelectedDevice() is { IsReady: true } d) await ResetDevicesAsync(new List<Device> { d });
        });
        _devicesGrid.ContextMenuStrip = deviceMenu;

        var logMenu = new ContextMenuStrip();
        logMenu.Items.Add("Копировать всё", null, (_, _) =>
        {
            var text = _log.AllText;
            if (text.Length > 0) Clipboard.SetText(text);
        });
        logMenu.Items.Add("Открыть папку логов", null, (_, _) => OpenFolder(AppInfo.LogsDir));
        _log.ContextMenuStrip = logMenu;
    }

    #endregion

    #region Events

    private void WireEvents()
    {
        Shown += async (_, _) =>
        {
            if (!AdbClient.AdbExists)
            {
                Log(SystemSource, $"Не найден adb: {AdbClient.AdbPath}");
                Dialogs.Show(this, $"Не найден adb.exe:\n{AdbClient.AdbPath}\n\nПереустановите программу.",
                                "PADLOck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (_profile.SecretsUnreadable)
            {
                Log(SystemSource, "Пароли в настройках прошивки не удалось расшифровать — их нужно ввести заново");
                Dialogs.Show(this,
                    "Пароли в настройках прошивки (Wi-Fi, PIN, MQTT) сохранены другим пользователем Windows " +
                    "или на другом компьютере, поэтому их нельзя расшифровать.\n\nВведите их заново в «Настройки прошивки».",
                    "PADLOck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await PollDevicesAsync();
            _pollTimer.Start();
        };
        FormClosing += (_, _) =>
        {
            _pollTimer.Stop();
            SaveWindow();
        };
        _pollTimer.Tick += async (_, _) => await PollDevicesAsync();

        _settingsButton.Click += async (_, _) => await OpenSettingsAsync();
        _advancedButton.Click += (_, _) => ToggleAdvanced();
        _provisionButton.Click += async (_, _) =>
        {
            if (_running) RequestStop();
            else await RunProvisionAsync();
        };
        _wifiButton.Click += async (_, _) => await RunWifiChangeAsync();
        _urlButton.Click += async (_, _) => await RunUrlChangeAsync();
        _instructionsButton.Click += (_, _) => OpenInstructions();
        _checkButton.Click += async (_, _) => await RunCheckAsync();
        _openLogsButton.Click += (_, _) => OpenFolder(AppInfo.LogsDir);

        _devicesGrid.CellPainting += PaintDeviceCell;
        _devicesGrid.CurrentCellDirtyStateChanged += CommitCheckbox;
        _devicesGrid.CellValueChanged += (_, e) =>
        {
            if (_suppressGridEvents || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            if (_devicesGrid.Rows[e.RowIndex].Tag is Device d)
            {
                d.IsChecked = _devicesGrid.Rows[e.RowIndex].Cells[0].Value is true;
                UpdateSelectionLabel();
            }
        };
        _devicesGrid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _devicesGrid.ClearSelection();
                _devicesGrid.Rows[e.RowIndex].Selected = true;
            }
        };
        _devicesGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex > 0 && _devicesGrid.Rows[e.RowIndex].Tag is Device d)
                await RenameDeviceAsync(d);
        };
        _devicesGrid.SelectionChanged += (_, _) => OnDeviceSelectionChanged();
        _selectAllDevices.Click += (_, _) => SetAllDevicesChecked(true);
        _selectNoDevices.Click += (_, _) => SetAllDevicesChecked(false);
        _resetButton.Click += async (_, _) => await ResetDevicesAsync(TargetDevices());
        _dropZone.FilesSelected += async files => await InstallApksAsync(files);

        _pkgGrid.CellPainting += PaintPackageCell;
        _pkgGrid.CurrentCellDirtyStateChanged += CommitCheckbox;
        _pkgGrid.CellValueChanged += (_, e) =>
        {
            if (_suppressGridEvents || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            if (_pkgGrid.Rows[e.RowIndex].Tag is PackageRow p && !p.IsLocked)
            {
                p.IsChecked = _pkgGrid.Rows[e.RowIndex].Cells[0].Value is true;
                UpdateSelectionLabel();
            }
        };
        _search.TextChanged += (_, _) => RebuildPackageGrid();
        _categoryFilter.SelectedIndexChanged += (_, _) => RebuildPackageGrid();
        _markRecommended.Click += (_, _) =>
            MarkPackages(p => p.Info.Category == PackageCategory.Remove && p.State != PackageState.Uninstalled);
        _clearMarks.Click += (_, _) => MarkPackages(_ => false);

        _removeButton.Click += async (_, _) => await RunActionAsync(PackageAction.Remove);
        _disableButton.Click += async (_, _) => await RunActionAsync(PackageAction.Disable);
        _restoreButton.Click += async (_, _) => await RunActionAsync(PackageAction.Restore);

        _logFilter.SelectedIndexChanged += (_, _) => RebuildLogView();
    }

    private static void CommitCheckbox(object? sender, EventArgs e)
    {
        if (sender is DataGridView g && g.IsCurrentCellDirty)
            g.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Не удалось открыть папку: " + ex.Message);
        }
    }

    #endregion

    #region Mode

    private void ToggleAdvanced()
    {
        if (!_settings.AdvancedMode)
        {
            var answer = Dialogs.Show(this,
                "В расширенном режиме доступно ручное удаление и отключение любых пакетов.\n\n" +
                "Удаление системных компонентов может сделать планшет незагружаемым до сброса к заводским настройкам.\n\n" +
                "Включить расширенный режим?",
                "Расширенный режим", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
                return;
        }

        _settings.AdvancedMode = !_settings.AdvancedMode;
        SaveSettings();
        ApplyMode();
        if (_settings.AdvancedMode)
            _ = LoadPackagesAsync();
    }

    private void ApplyMode()
    {
        var advanced = _settings.AdvancedMode;
        WithoutRedraw(this, () =>
        {
            _packagesCard.Visible = advanced;
            _profilesButton.Visible = advanced;
            _advancedButton.Text = advanced ? "Обычный режим" : "Расширенный режим";
            _advancedButton.Width = TextRenderer.MeasureText(_advancedButton.Text, Theme.BoldFont).Width + 36;

            _body.SuspendLayout();
            _body.RowStyles[0].SizeType = advanced ? SizeType.Percent : SizeType.Absolute;
            _body.RowStyles[0].Height = advanced ? 62 : 0;
            _body.RowStyles[1].SizeType = SizeType.Percent;
            _body.RowStyles[1].Height = advanced ? 38 : 100;
            _body.ResumeLayout(true);
        });
    }

    #endregion

    #region Catalog

    private void LoadCatalog()
    {
        _catalog = PackageCatalog.LoadFolders(new[] { AppInfo.ProgramProfilesDir, AppInfo.UserProfilesDir },
                                              error => Log(SystemSource, "Ошибка базы пакетов " + error));
        Log(SystemSource, $"Базы пакетов: {_catalog.FileCount} файлов, {_catalog.Count} пакетов");
    }

    private void ExportUnknownPackages()
    {
        if (_reference is null)
        {
            Dialogs.Show(this, "Подключите планшет.", "PADLOck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var unknown = _packages.Where(p => p.Info.Category == PackageCategory.Unknown).Select(p => p.Name).ToList();
        if (unknown.Count == 0)
        {
            Dialogs.Show(this, "Все пакеты этого планшета уже есть в базах.", "PADLOck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var baseName = string.Concat($"{_reference.Manufacturer}-{_reference.Model}".ToLowerInvariant()
                                     .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')).Trim('-');
        if (baseName.Length == 0) baseName = "device";

        var path = Path.Combine(AppInfo.UserProfilesDir, baseName + ".json");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(AppInfo.UserProfilesDir, $"{baseName}-{i}.json");

        try
        {
            PackageCatalog.WriteTemplate(path, $"{_reference.Manufacturer} {_reference.Model}".Trim(), unknown);
            Log(SystemSource, $"Сохранено {unknown.Count} пакетов без описания: {path}");
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Не удалось сохранить файл: " + ex.Message);
        }
    }

    #endregion

    #region Devices

    private async Task PollDevicesAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var entries = await AdbDevices.ListAsync();
            var changed = false;

            foreach (var d in _devices.ToList())
            {
                if (entries.Any(x => x.TransportId == d.TransportId)) continue;

                if (d.IsBusy)
                {
                    if (d.State != "disconnected")
                    {
                        d.State = "disconnected";
                        changed = true;
                    }
                }
                else
                {
                    _devices.Remove(d);
                    changed = true;
                    Log(SystemSource, $"Отключён: {d.DisplayName}");
                }
            }

            foreach (var entry in entries)
            {
                var d = _devices.FirstOrDefault(x => x.TransportId == entry.TransportId);
                if (d is null)
                {
                    d = new Device(entry.Serial, entry.TransportId) { Model = entry.Model, State = entry.State };
                    _devices.Add(d);
                    changed = true;
                    Log(SystemSource, $"Подключён: {d.DisplayName} ({entry.State})");
                    if (!_logFilter.Items.Contains(entry.Serial))
                        _logFilter.Items.Add(entry.Serial);
                }
                else if (d.State != entry.State)
                {
                    d.State = entry.State;
                    if (entry.Model.Length > 0) d.Model = entry.Model;
                    changed = true;
                }

                if (d.IsReady && !d.InfoLoaded && !d.InfoLoading && !d.IsBusy)
                    _ = LoadDeviceInfoAsync(d);
            }

            if (changed)
            {
                SyncDeviceRows();
                EnsureReference();
            }
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Ошибка опроса adb: " + ex.Message);
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task LoadDeviceInfoAsync(Device d)
    {
        d.InfoLoading = true;
        try
        {
            // Пока Android загружается, службы недоступны — попробуем на следующем опросе
            var quiet = new AdbClient(d.TransportId, null);
            if (!await DeviceQueries.IsBootCompletedAsync(quiet))
                return;

            var props = await DeviceQueries.ReadInfoAsync(quiet);
            d.AndroidVersion = props.AndroidVersion;
            d.Manufacturer = props.Manufacturer;
            d.Name = props.Name;
            d.DeviceOwner = props.DeviceOwner;
            d.InfoLoaded = true;
            _devicesGrid.Invalidate();

            if (d == _reference)
            {
                UpdateReferenceLabel();
                _ = LoadPackagesAsync();
            }
        }
        catch (Exception ex)
        {
            Log(d.Serial, "Не удалось прочитать сведения: " + ex.Message);
        }
        finally
        {
            d.InfoLoading = false;
        }
    }

    private void SyncDeviceRows()
    {
        _suppressGridEvents = true;
        try
        {
            for (int i = _devicesGrid.Rows.Count - 1; i >= 0; i--)
                if (_devicesGrid.Rows[i].Tag is Device d && !_devices.Contains(d))
                    _devicesGrid.Rows.RemoveAt(i);

            var shown = _devicesGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).ToHashSet();
            foreach (var d in _devices.Where(x => !shown.Contains(x)))
            {
                int i = _devicesGrid.Rows.Add(d.IsChecked, "", "");
                _devicesGrid.Rows[i].Tag = d;
            }

            foreach (DataGridViewRow row in _devicesGrid.Rows)
                if (row.Tag is Device d)
                    row.Cells[0].ReadOnly = !d.IsReady;
        }
        finally
        {
            _suppressGridEvents = false;
        }

        _devicesCount.Text = _devices.Count == 0 ? "нет подключённых" : $"подключено: {_devices.Count}";
        _devicesGrid.Invalidate();
        UpdateSelectionLabel();
    }

    private void SetAllDevicesChecked(bool value)
    {
        _suppressGridEvents = true;
        foreach (DataGridViewRow row in _devicesGrid.Rows)
        {
            if (row.Tag is Device d && d.IsReady)
            {
                d.IsChecked = value;
                row.Cells[0].Value = value;
            }
        }
        _suppressGridEvents = false;
        UpdateSelectionLabel();
    }

    private Device? SelectedDevice() =>
        _devicesGrid.SelectedRows.Count > 0 ? _devicesGrid.SelectedRows[0].Tag as Device : null;

    private async Task RenameDeviceAsync(Device d)
    {
        if (!d.IsReady || d.IsBusy) return;

        using var dialog = new InputDialog("Имя планшета", $"Имя для {d.Serial} (сохраняется на планшете):", d.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var name = dialog.Value;
        if (name == d.Name) return;

        if (await DeviceQueries.SetNameAsync(ClientFor(d), name))
        {
            d.Name = name;
            Log(d.Serial, name.Length > 0 ? $"Новое имя: {name}" : "Имя сброшено");
            _devicesGrid.Invalidate();
            if (d == _reference) UpdateReferenceLabel();
        }
        else
        {
            Log(d.Serial, "Не удалось сохранить имя");
        }
    }

    #endregion

    #region Packages

    private void OnDeviceSelectionChanged()
    {
        if (SelectedDevice() is not { } d || !d.IsReady || d == _reference) return;
        _reference = d;
        _ = LoadPackagesAsync();
    }

    private void EnsureReference()
    {
        if (_reference is not null && _devices.Contains(_reference) && _reference.IsReady)
            return;

        _reference = _devices.FirstOrDefault(d => d.IsReady);
        var row = _devicesGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == _reference);
        if (row is not null)
        {
            _devicesGrid.ClearSelection();
            row.Selected = true;
        }
        _ = LoadPackagesAsync();
    }

    // Таблица пакетов нужна только в расширенном режиме — в обычном планшет лишний раз не опрашивается
    private async Task LoadPackagesAsync()
    {
        var d = _reference;
        var version = ++_packageLoadVersion;

        if (d is null)
        {
            _packages = new();
            RebuildPackageGrid();
            UpdateReferenceLabel();
            return;
        }
        if (!_settings.AdvancedMode || !d.InfoLoaded)
        {
            UpdateReferenceLabel();
            return;
        }

        UpdateReferenceLabel(loading: true);
        try
        {
            var list = await DeviceQueries.ReadPackagesAsync(new AdbClient(d.TransportId, null));
            if (version != _packageLoadVersion) return;

            var checkedNames = _packages.Where(p => p.IsChecked).Select(p => p.Name).ToHashSet();
            _packages = list.Select(x =>
            {
                var row = new PackageRow(x.Name, _catalog.Get(x.Name), x.State);
                row.IsChecked = !row.IsLocked && checkedNames.Contains(x.Name);
                return row;
            }).ToList();

            RebuildPackageGrid();
            UpdateReferenceLabel();
        }
        catch (Exception ex)
        {
            Log(d.Serial, "Ошибка чтения пакетов: " + ex.Message);
            UpdateReferenceLabel();
        }
    }

    private void MarkPackages(Func<PackageRow, bool> predicate)
    {
        foreach (var p in _packages)
            p.IsChecked = !p.IsLocked && predicate(p);
        RebuildPackageGrid();
    }

    private void RebuildPackageGrid()
    {
        var term = _search.Text.Trim();
        var category = CategoryFilters[Math.Max(0, _categoryFilter.SelectedIndex)].Category;

        var visible = _packages
            .Where(p => category is null || p.Info.Category == category)
            .Where(p => term.Length == 0
                        || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || p.Info.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || p.Info.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Info.Category)
            .ThenBy(p => p.Name);

        _suppressGridEvents = true;
        _pkgGrid.SuspendLayout();
        try
        {
            _pkgGrid.Rows.Clear();
            foreach (var p in visible)
            {
                int i = _pkgGrid.Rows.Add(p.IsChecked, p.Info.Title, p.Name, Theme.CategoryText(p.Info.Category), p.Info.Description, StateText(p.State));
                var row = _pkgGrid.Rows[i];
                row.Tag = p;
                if (p.IsLocked) row.Cells[0].ReadOnly = true;
                if (p.State != PackageState.Installed) row.DefaultCellStyle.ForeColor = Theme.Dimmed;
            }
        }
        finally
        {
            _pkgGrid.ResumeLayout();
            _suppressGridEvents = false;
        }
        UpdateSelectionLabel();
    }

    private void UpdateReferenceLabel(bool loading = false)
    {
        if (_reference is null)
        {
            _referenceLabel.Text = "подключите планшет";
            return;
        }
        var owner = _reference.DeviceOwner.Length == 0 ? "…" : _reference.DeviceOwner;
        _referenceLabel.Text = $"по планшету: {_reference.DisplayName}   ·   Device Owner: {owner}" + (loading ? "   ·   загрузка…" : "");
    }

    private void UpdateSelectionLabel()
    {
        var packages = _packages.Count(p => p.IsChecked);
        var devices = _devices.Count(d => d.IsChecked && d.IsReady);
        _selectionLabel.Text = $"Отмечено пакетов: {packages}   ·   планшетов: {devices}";
    }

    private static string StateText(PackageState s) => s switch
    {
        PackageState.Installed => "Установлен",
        PackageState.Disabled => "Отключён",
        PackageState.Uninstalled => "Удалён",
        _ => s.ToString()
    };

    #endregion

    #region Package actions

    private List<Device> TargetDevices() =>
        _devices.Where(d => d.IsChecked && d.IsReady && !d.IsBusy).ToList();

    private bool NoDevicesSelected(List<Device> devices)
    {
        if (devices.Count > 0) return false;
        Dialogs.Show(this, "Отметьте хотя бы один подключённый планшет.", "PADLOck",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    private async Task RunActionAsync(PackageAction action)
    {
        if (_running) return;

        var selected = _packages.Where(p => p.IsChecked && !p.IsLocked).ToList();
        var devices = TargetDevices();
        if (selected.Count == 0 || devices.Count == 0)
        {
            Dialogs.Show(this, "Отметьте хотя бы один пакет и хотя бы один планшет.", "PADLOck",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var verb = action switch
        {
            PackageAction.Remove => "Удалить",
            PackageAction.Disable => "Отключить",
            _ => "Восстановить"
        };
        var question = $"{verb} пакетов: {selected.Count}\nна планшетах: {devices.Count}?";
        if (action != PackageAction.Restore)
        {
            var risky = selected.Count(p => p.Info.Category is PackageCategory.Keep or PackageCategory.Unknown);
            if (risky > 0)
                question += $"\n\nИз них {risky} с категорией «Оставить» или «Нет данных» — они могут быть нужны системе.";
            question += "\n\nДействие обратимо кнопкой «Восстановить».";
        }

        if (Dialogs.Show(this, question, "Подтверждение", MessageBoxButtons.YesNo,
                            action == PackageAction.Restore ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var packages = selected.Select(p => p.Name).ToList();
        SetRunning(true);
        Log(SystemSource, $"{verb}: {packages.Count} пакетов на {devices.Count} планшетах");
        try
        {
            await Task.WhenAll(devices.Select(d => RunPackagesOnDeviceAsync(d, packages, action)));
        }
        finally
        {
            SetRunning(false);
        }
        await LoadPackagesAsync();
    }

    private async Task RunPackagesOnDeviceAsync(Device d, List<string> packages, PackageAction action)
    {
        BeginWork(d, "Выполняется", packages.Count);
        int ok = 0, already = 0, skipped = 0;
        var adb = ClientFor(d);
        try
        {
            foreach (var pkg in packages)
            {
                if (StopRequested)
                {
                    Log(d.Serial, "Остановлено");
                    break;
                }
                if (d.State == "disconnected")
                {
                    d.Errors++;
                    Log(d.Serial, "Планшет отключён, остановлено");
                    break;
                }

                var outcome = await PackageOperations.ApplyAsync(adb, pkg, action, _catalog.Get(pkg), d.IsKioskOwner);
                switch (outcome.Kind)
                {
                    case OutcomeKind.Ok: ok++; Log(d.Serial, $"OK    {pkg}"); break;
                    case OutcomeKind.AlreadyDone: already++; Log(d.Serial, $"==    {pkg}"); break;
                    case OutcomeKind.Skipped: skipped++; Log(d.Serial, $"SKIP  {pkg}: {outcome.Message}"); break;
                    default: d.Errors++; Log(d.Serial, $"ERR   {pkg}: {outcome.Message}"); break;
                }
                d.Done++;
                _devicesGrid.Invalidate();
            }
        }
        catch (Exception ex)
        {
            d.Errors++;
            Log(d.Serial, "Ошибка: " + ex.Message);
        }
        finally
        {
            EndWork(d);
            Log(d.Serial, $"Итог: успешно {ok}, уже было {already}, пропущено {skipped}, ошибок {d.Errors}");
        }
    }

    private async Task InstallApksAsync(string[] files)
    {
        if (_running)
        {
            Log(SystemSource, "Дождитесь завершения текущей операции");
            return;
        }

        var devices = TargetDevices();
        if (NoDevicesSelected(devices)) return;

        var names = string.Join("\n", files.Select(Path.GetFileName));
        if (Dialogs.Show(this, $"Установить на планшетов: {devices.Count}?\n\n{names}", "Установка APK",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetRunning(true);
        Log(SystemSource, $"Установка {files.Length} APK на {devices.Count} планшетах");
        try
        {
            await Task.WhenAll(devices.Select(d => InstallOnDeviceAsync(d, files)));
        }
        finally
        {
            SetRunning(false);
        }
        await LoadPackagesAsync();
    }

    private async Task InstallOnDeviceAsync(Device d, string[] files)
    {
        BeginWork(d, "Установка", files.Length);
        int ok = 0;
        var adb = ClientFor(d);
        try
        {
            foreach (var file in files)
            {
                if (StopRequested)
                {
                    Log(d.Serial, "Остановлено");
                    break;
                }
                if (d.State == "disconnected")
                {
                    d.Errors++;
                    Log(d.Serial, "Планшет отключён, остановлено");
                    break;
                }

                await _installSlots.WaitAsync(StopToken);
                Outcome outcome;
                try
                {
                    outcome = await PackageOperations.InstallApkAsync(adb, file);
                }
                finally
                {
                    _installSlots.Release();
                }

                if (outcome.Kind == OutcomeKind.Ok)
                {
                    ok++;
                    Log(d.Serial, $"OK    {Path.GetFileName(file)}");
                }
                else
                {
                    d.Errors++;
                    Log(d.Serial, $"ERR   {Path.GetFileName(file)}: {outcome.Message}");
                }
                d.Done++;
                _devicesGrid.Invalidate();
            }
        }
        catch (Exception ex)
        {
            d.Errors++;
            Log(d.Serial, "Ошибка: " + ex.Message);
        }
        finally
        {
            EndWork(d);
            Log(d.Serial, $"Итог установки: успешно {ok}, ошибок {d.Errors}");
        }
    }

    private void BeginWork(Device d, string activity, int total)
    {
        d.IsBusy = true;
        d.Activity = activity;
        d.Done = 0;
        d.Errors = 0;
        d.Total = total;
        _devicesGrid.Invalidate();
    }

    private void EndWork(Device d)
    {
        d.IsBusy = false;
        _devicesGrid.Invalidate();
    }

    private void SetRunning(bool running)
    {
        _running = running;
        foreach (var b in new[] { _removeButton, _disableButton, _restoreButton, _markRecommended, _clearMarks,
                                  _selectAllDevices, _selectNoDevices, _resetButton, _wifiButton,
                                  _urlButton, _checkButton, _settingsButton, _advancedButton, _updatesButton })
            b.Enabled = !running;
        _dropZone.Enabled = !running;

        // Во время любой операции «Прошить» превращается в «Стоп»
        if (running)
        {
            _stop = new CancellationTokenSource();
            _provisionButton.Text = "Стоп";
            Theme.SetKind(_provisionButton, ButtonKind.Danger);
            _tips.SetToolTip(_provisionButton, "Остановить текущую операцию");
        }
        else
        {
            // Отменённый токен не должен достаться следующим командам (переименование и т.п.)
            _stop = null;
            _provisionButton.Text = "Прошить";
            Theme.SetKind(_provisionButton, ButtonKind.Primary);
            _tips.SetToolTip(_provisionButton, "Прошивка отмеченных планшетов: полностью или выбранные шаги");
        }
        _provisionButton.Enabled = true;
    }

    private void RequestStop()
    {
        if (_stop is null || _stop.IsCancellationRequested) return;
        _stop.Cancel();
        _provisionButton.Enabled = false;
        _provisionButton.Text = "Остановка…";
        Log(SystemSource, "Остановка по кнопке «Стоп»: текущие команды прерываются");
    }

    private AdbClient ClientFor(Device d) => new(d.TransportId, text => Log(d.Serial, text), StopToken);

    private CancellationToken StopToken => _stop?.Token ?? CancellationToken.None;
    private bool StopRequested => _stop?.IsCancellationRequested == true;

    #endregion

    #region Factory reset

    // Прошитый планшет FreeKiosk защищает от сброса (ограничение Device Owner), и снять его через adb нельзя.
    // Поэтому автоматически сбрасывается только планшет без владельца, остальные — через recovery.
    private async Task ResetDevicesAsync(List<Device> devices)
    {
        if (_running) return;
        if (NoDevicesSelected(devices)) return;

        var names = string.Join("\n", devices.Take(10).Select(d => "• " + d.DisplayName));
        if (devices.Count > 10) names += $"\n… и ещё {devices.Count - 10}";
        if (Dialogs.Show(this,
                $"Сбросить к заводским настройкам планшетов: {devices.Count}?\n\n{names}\n\nВсе данные и настройки на них будут удалены.",
                "Сброс к заводским", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetRunning(true);
        Log(SystemSource, $"Сброс к заводским: {devices.Count} планшетов");
        bool[] needRecovery;
        try
        {
            needRecovery = await Task.WhenAll(devices.Select(ResetOneAsync));
        }
        finally
        {
            SetRunning(false);
        }

        var recovery = needRecovery.Count(x => x);
        if (recovery > 0)
            Dialogs.Show(this,
                $"Планшетов в режиме восстановления (recovery): {recovery}.\n\n" +
                "На экране каждого планшета: кнопки громкости — выбор, кнопка питания — подтверждение.\n\n" +
                "1. Wipe data/factory reset → подтвердите (Factory data reset / Yes).\n" +
                "2. Дождитесь окончания и выберите Reboot system now.\n\n" +
                "Если на экране надпись «Нет команды» — зажмите питание и один раз нажмите громкость вверх.\n\n" +
                "После сброса подготовьте планшеты заново (инструкция, пункт 1).",
                "Сброс к заводским", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Возвращает true, если планшет отправлен в recovery и нужен сброс кнопками
    private async Task<bool> ResetOneAsync(Device d)
    {
        var adb = ClientFor(d);
        BeginWork(d, "Сброс", 1);
        Log(d.Serial, "-- Сброс к заводским настройкам");
        try
        {
            var owner = await KioskProvisioner.ReadOwnerAsync(adb);
            if (owner.Length == 0)
            {
                await adb.ShellAsync("am broadcast -a android.intent.action.FACTORY_RESET -p android --receiver-foreground");
                if (await WaitForDisconnectAsync(d, TimeSpan.FromSeconds(15)))
                {
                    Log(d.Serial, "OK    Планшет перезагружается и сбрасывается");
                    return false;
                }
                Log(d.Serial, "  прошивка не разрешает сброс из adb — перевожу в режим восстановления");
            }
            else
            {
                Log(d.Serial, $"  сброс заблокирован владельцем устройства ({owner}) — перевожу в режим восстановления");
            }

            await adb.RunAsync("reboot recovery");
            Log(d.Serial, "OK    Отправлен в режим восстановления");
            return true;
        }
        catch (Exception ex)
        {
            d.Errors++;
            Log(d.Serial, "  ошибка: " + ex.Message);
            return false;
        }
        finally
        {
            d.Done = 1;
            EndWork(d);
        }
    }

    private async Task<bool> WaitForDisconnectAsync(Device d, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !StopRequested)
        {
            await Task.Delay(1000);
            var devices = await AdbDevices.ListAsync();
            if (!devices.Any(e => e.TransportId == d.TransportId && e.State == "device"))
                return true;
        }
        return false;
    }

    #endregion

    #region Updates

    // Обновление программы и APK FreeKiosk из GitHub Releases.
    // manual — вызвано из меню: сообщаем результат, даже если обновлений нет
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_updating || (manual && _running)) return;
        _updating = true;
        try
        {
            var channel = _settings.UpdateDevChannel ? "dev" : "stable";
            ReleaseInfo? release;
            try
            {
                release = await _updateService.FindLatestAsync(_settings.UpdateDevChannel, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var reason = ex is UpdateException ? ex.Message : $"нет связи с GitHub ({ex.Message})";
                Log(SystemSource, "Обновления: " + reason);
                if (manual)
                    Dialogs.Show(this, "Не удалось проверить обновления:\n" + reason, "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (release is null)
            {
                Log(SystemSource, $"Обновления: в канале {channel} релизов нет");
                if (manual)
                    Dialogs.Show(this, $"В канале {channel} пока нет релизов.", "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var newer = SemVersion.TryParse(AppInfo.Version, out var current) && release.Version.CompareTo(current) > 0;
            if (newer && release.Installer is null)
                Log(SystemSource, $"Обновления: в релизе {release.Tag} нет установщика PADLOck-Setup-*.exe");
            else if (newer)
            {
                var recentlyFailed = _settings.LastUpdateAttempt == release.Tag
                                     && DateTime.UtcNow - _settings.LastUpdateAttemptUtc < TimeSpan.FromMinutes(30);
                if (recentlyFailed && !manual)
                    Log(SystemSource, $"Обновления: {release.Tag} недавно не установилось — повторю позже или через меню «Обновления»");
                else if (await InstallUpdateAsync(release))
                    return;
            }

            var apkUpdated = await SyncApkAsync(release);
            if (manual && !newer && !apkUpdated)
            {
                var apk = _profile.FindNewestApk();
                Dialogs.Show(this,
                    $"Установлена последняя версия PADLOck {AppInfo.Version} (канал {channel}).\n" +
                    $"FreeKiosk для прошивки: {(apk is null ? "нет" : Path.GetFileName(apk))}",
                    "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // Обновление не начинается посреди прошивки или пока открыто другое окно
    private async Task WaitIdleAsync()
    {
        while (_running || Application.OpenForms.Cast<Form>().Any(f => f != this && f.Modal))
            await Task.Delay(1000);
    }

    // true — установщик запущен, программа закрывается
    private async Task<bool> InstallUpdateAsync(ReleaseInfo release)
    {
        await WaitIdleAsync();
        Log(SystemSource, $"Обновления: доступна версия {release.Version}{(release.Prerelease ? " (dev)" : "")}, скачиваю");

        CleanDirectory(AppInfo.UpdatesDir, "*");
        var path = Path.Combine(AppInfo.UpdatesDir, release.Installer!.Name);
        using (var dialog = new UpdateDialog("Обновление PADLOck",
                   $"Версия {AppInfo.Version} → {release.Version}{(release.Prerelease ? "  ·  тестовая (dev)" : "")}",
                   release.Notes, release.Installer, path, _updateService))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                Log(SystemSource, "Обновления: " + (dialog.Error ?? "скачивание отменено"));
                return false;
            }
        }

        _settings.LastUpdateAttempt = release.Tag;
        _settings.LastUpdateAttemptUtc = DateTime.UtcNow;
        SaveSettings();

        // Установщик сам запросит права администратора, покажет ход установки и снова запустит программу
        try
        {
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NOCANCEL")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Обновления: установщик не запустился — " + ex.Message);
            Dialogs.Show(this, "Не удалось запустить установку обновления:\n" + ex.Message, "Обновления",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        Log(SystemSource, $"Обновления: установка {release.Version}, программа перезапустится");
        Close();
        return true;
    }

    // APK FreeKiosk из релиза: скачивается, если его ещё нет или он другой. true — скачан новый
    private async Task<bool> SyncApkAsync(ReleaseInfo release)
    {
        if (release.Apk is null)
            return false;
        if (_profile.ApkFolder.Trim().Length > 0)
        {
            Log(SystemSource, $"Обновления: в релизе {release.Apk.Name}, но в настройках указана своя папка с APK — прошивка идёт из неё");
            return false;
        }

        var destination = Path.Combine(AppInfo.ReleaseApkDir, release.Apk.Name);
        if (File.Exists(destination) && release.Apk.Sha256 is { } expected
            && await Task.Run(() => UpdateService.Sha256Of(destination)) == expected)
        {
            CleanDirectory(AppInfo.ReleaseApkDir, "*.apk", keep: destination);
            return false;
        }

        await WaitIdleAsync();
        Log(SystemSource, $"Обновления: FreeKiosk для прошивки — {release.Apk.Name}, скачиваю");
        using var dialog = new UpdateDialog("Загрузка FreeKiosk",
            $"{release.Apk.Name} — этой версией будут прошиваться планшеты (релиз {release.Tag})",
            "", release.Apk, destination, _updateService);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            Log(SystemSource, "Обновления: " + (dialog.Error ?? "скачивание FreeKiosk отменено"));
            return false;
        }

        CleanDirectory(AppInfo.ReleaseApkDir, "*.apk", keep: destination);
        Log(SystemSource, $"Обновления: FreeKiosk {release.Apk.Name} готов к прошивке");
        UpdateProvisionInfo();
        return true;
    }

    private void CleanDirectory(string dir, string pattern, string? keep = null)
    {
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(dir, pattern))
                if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(SystemSource, "Обновления: не удалось убрать старые файлы — " + ex.Message);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    #endregion

    #region Provisioning

    private void UpdateProvisionInfo()
    {
        var apk = _profile.FindNewestApk();
        var wifi = string.IsNullOrWhiteSpace(_profile.WifiSsid) ? "не задан" : _profile.WifiSsid;
        var kiosk = !_profile.KioskConfigured ? "не настроен"
                  : _profile.KioskMode == "app" ? _profile.KioskApp
                  : _profile.KioskUrl;
        _provisionInfo.Text =
            $"APK: {(apk is null ? "нет — «Обновления → Проверить обновления»" : Path.GetFileName(apk))}\n" +
            $"Киоск: {kiosk}\n" +
            $"Wi-Fi: {wifi} · {_profile.TimeZone}";
    }

    private string DeviceNameFor(Device d) => d.Name.Length > 0 ? d.Name : d.Serial;

    private async Task OpenSettingsAsync()
    {
        var before = _profile.Clone();
        using var dialog = new SettingsDialog(_profile);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SaveProfile();

        if (dialog.ApplyRequested)
            await RunProvisionAsync(KioskProvisioner.KeysFor(_profile.ChangedGroups(before)));
    }

    private void SaveProfile()
    {
        try
        {
            _profile.Save();
            Log(SystemSource, "Настройки прошивки сохранены");
        }
        catch (Exception ex)
        {
            Log(SystemSource, "Не удалось сохранить настройки прошивки: " + ex.Message);
        }
        UpdateProvisionInfo();
    }

    private void OpenInstructions()
    {
        if (_instructions is { IsDisposed: false })
        {
            if (_instructions.WindowState == FormWindowState.Minimized)
                _instructions.WindowState = FormWindowState.Normal;
            _instructions.Activate();
            return;
        }
        _instructions = new InstructionsForm();
        _instructions.Show(this);
    }

    private string? RequireApk()
    {
        var apk = _profile.FindNewestApk();
        if (apk is null)
            Dialogs.Show(this, "Нет APK FreeKiosk.\n\nОн скачивается вместе с обновлением программы: «Обновления → Проверить обновления». " +
                                  "Либо укажите свою папку с APK в «Настройки прошивки → Киоск».",
                            "PADLOck", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return apk;
    }

    // changedOnly = null — прошивка (отмечено всё), иначе — применение изменённых настроек (отмечены только они)
    private async Task RunProvisionAsync(IReadOnlySet<StepKey>? changedOnly = null)
    {
        if (_running) return;

        UpdateProvisionInfo();
        var devices = TargetDevices();
        if (NoDevicesSelected(devices)) return;

        var steps = KioskProvisioner.Describe(_profile);
        var available = steps.Select(s => s.Key).ToHashSet();
        HashSet<StepKey> preselected;
        if (changedOnly is null)
        {
            preselected = available.ToHashSet();
            if (!_profile.MqttEnabled) preselected.Remove(StepKey.Mqtt);
        }
        else
        {
            preselected = changedOnly.Where(available.Contains).ToHashSet();
            preselected.Add(StepKey.Check);
        }
        if (_profile.RebootWhenDone) preselected.Add(StepKey.Reboot);
        else preselected.Remove(StepKey.Reboot);

        var apkName = _profile.FindNewestApk() is { } found ? Path.GetFileName(found) : "не найден";
        var summary = $"Отмеченных планшетов: {devices.Count}   ·   APK: {apkName}\n\n" +
                      "Отметьте, что выполнить. Уже сделанное (та же версия FreeKiosk, Device Owner, " +
                      "удалённые приложения) проверяется и пропускается, поэтому повторная прошивка идёт быстро.";
        HashSet<StepKey> keys;
        using (var dialog = new StepsDialog(changedOnly is null ? "Прошивка под киоск" : "Применить изменения",
                                            summary, changedOnly is null ? "Прошить" : "Применить", steps, preselected))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            keys = dialog.Selected;
        }

        // Без Device Owner киоск не заблокирован — непрошитым планшетам предлагаем добавить базовые шаги
        var fresh = new List<Device>();
        if (!keys.Contains(StepKey.Owner))
        {
            var owners = await Task.WhenAll(devices.Select(d => KioskProvisioner.ReadOwnerAsync(new AdbClient(d.TransportId, null))));
            var notOwned = devices.Where((d, i) => owners[i] != KioskProvisioner.KioskPackage).ToList();
            if (notOwned.Count > 0)
            {
                var answer = Dialogs.Show(this,
                    $"Не прошиты (FreeKiosk не владелец устройства): {string.Join(", ", notOwned.Select(d => d.Title))}.\n\n" +
                    "Без этого киоск на них не будет заблокирован.\n\n" +
                    "Да — добавить им установку FreeKiosk и Device Owner\n" +
                    "Нет — выполнить только отмеченное\n" +
                    "Отмена — ничего не делать",
                    "Непрошитые планшеты", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel)
                    return;
                if (answer == DialogResult.Yes)
                    fresh = notOwned;
            }
        }

        string? apk = null;
        if (keys.Contains(StepKey.Install) || fresh.Count > 0)
        {
            apk = RequireApk();
            if (apk is null) return;
        }
        if ((keys.Contains(StepKey.Kiosk) || keys.Contains(StepKey.Mqtt)) && !RequirePin())
            return;

        var titles = steps.Where(s => keys.Contains(s.Key)).Select(s => s.Title);
        await ExecuteAsync($"Прошивка {devices.Count} планшетов: {string.Join(", ", titles)}", "Прошивка", devices, _profile, apk,
            (p, d) => p.AllSteps().Where(s => keys.Contains(s.Key)
                                              || (fresh.Contains(d) && KioskProvisioner.BaseKeys.Contains(s.Key))).ToList(),
            verify: true);
    }

    private async Task RunWifiChangeAsync()
    {
        if (_running) return;
        var devices = TargetDevices();
        if (NoDevicesSelected(devices)) return;

        using var dialog = new WifiDialog(_profile, devices.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var profile = _profile.Clone();
        foreach (var target in dialog.SaveToProfile ? new[] { profile, _profile } : new[] { profile })
        {
            target.WifiSsid = dialog.Ssid;
            target.WifiPassword = dialog.Password;
            target.WifiSecurity = dialog.Security;
            target.WifiForgetOthers = dialog.ForgetOthers;
        }
        if (dialog.SaveToProfile)
            SaveProfile();

        await ExecuteAsync($"Смена Wi-Fi на {devices.Count} планшетах: «{dialog.Ssid}»", "Wi-Fi", devices, profile, null,
                           (p, _) => p.WifiOnlySteps(), verify: false, waitWifi: true);
    }

    private async Task RunUrlChangeAsync()
    {
        if (_running) return;
        var devices = TargetDevices();
        if (NoDevicesSelected(devices)) return;
        if (!RequirePin()) return;

        using var dialog = new UrlDialog(_profile, devices.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var profile = _profile.Clone();
        profile.KioskMode = "web";
        profile.KioskUrl = dialog.Url;
        if (dialog.SaveToProfile)
        {
            _profile.KioskMode = "web";
            _profile.KioskUrl = dialog.Url;
            SaveProfile();
        }

        await ExecuteAsync($"Смена адреса на {devices.Count} планшетах: {dialog.Url}", "URL", devices, profile, null,
                           (p, _) => p.UrlOnlySteps(), verify: false);
    }

    private bool RequirePin()
    {
        if (_profile.KioskPin.Length > 0)
            return true;
        Dialogs.Show(this, "Нужен PIN киоска — укажите его в «Настройки прошивки → Киоск».", "PADLOck",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    // Выполнить шаги на всех планшетах параллельно. verify — дождаться загрузки и проверить планшеты
    private async Task ExecuteAsync(string title, string activity, List<Device> devices, KioskProfile profile, string? apk,
                                    Func<KioskProvisioner, Device, List<ProvisionStep>> stepsFor, bool verify, bool waitWifi = false)
    {
        SetRunning(true);
        Log(SystemSource, title);
        List<(Device Device, bool Rebooted, bool Failed)> runs;
        try
        {
            runs = (await Task.WhenAll(devices.Select(async d =>
            {
                var provisioner = new KioskProvisioner(ClientFor(d), profile, apk, _catalog, _installSlots,
                                                       DeviceNameFor(d), text => Log(d.Serial, text)) { WaitForWifi = waitWifi };
                var rebooted = await RunStepsAsync(d, activity, stepsFor(provisioner, d));
                return (d, rebooted, d.Errors > 0);
            }))).ToList();
            if (verify)
                await VerifyAfterAsync(runs);
        }
        finally
        {
            SetRunning(false);
        }

        if (!verify && !StopRequested)
        {
            var ok = runs.All(r => !r.Failed);
            Dialogs.Result(this,
                ok ? "Изменения прошли успешно" : "Изменения применены не везде",
                ok ? "" : "Подробности по планшетам с ошибкой — в журнале.",
                runs.Select(r => new ResultRow(r.Device.DisplayName, r.Failed ? "Ошибка" : "Готово", !r.Failed)).ToList());
        }
        await LoadPackagesAsync();
    }

    private async Task RunCheckAsync()
    {
        if (_running) return;

        var devices = TargetDevices();
        if (NoDevicesSelected(devices)) return;

        SetRunning(true);
        Log(SystemSource, $"Проверка {devices.Count} планшетов");
        var results = new List<(string Device, List<CheckItem> Items)>();
        try
        {
            var tasks = devices.Select(async d =>
            {
                BeginWork(d, "Проверка", 1);
                try
                {
                    var items = await DeviceChecker.RunAsync(new AdbClient(d.TransportId, null, StopToken), _profile);
                    d.Errors = items.Count(i => !i.Ok);
                    return (d.DisplayName, items);
                }
                catch (Exception ex)
                {
                    d.Errors = 1;
                    return (d.DisplayName, new List<CheckItem> { new("Связь с планшетом", false, ex.Message) });
                }
                finally
                {
                    d.Done = 1;
                    EndWork(d);
                }
            });
            results.AddRange(await Task.WhenAll(tasks));
        }
        finally
        {
            SetRunning(false);
        }

        foreach (var (device, items) in results)
        {
            var failed = items.Where(i => !i.Ok).ToList();
            Log(SystemSource, failed.Count == 0
                ? $"Проверка {device}: OK"
                : $"Проверка {device}: ошибка — {string.Join("; ", failed.Select(i => $"{i.Title}: {i.Details}"))}");
        }

        using var dialog = new CheckResultsDialog(results);
        dialog.ShowDialog(this);
    }

    // Возвращает true, если планшет ушёл в перезагрузку
    private async Task<bool> RunStepsAsync(Device d, string activity, List<ProvisionStep> steps)
    {
        var rebooted = false;
        BeginWork(d, activity, steps.Count);
        try
        {
            foreach (var step in steps)
            {
                if (StopRequested)
                {
                    d.Errors++;
                    Log(d.Serial, "Остановлено пользователем");
                    break;
                }
                if (d.State == "disconnected")
                {
                    d.Errors++;
                    Log(d.Serial, "Планшет отключён, выполнение остановлено");
                    break;
                }

                Log(d.Serial, $"-- {step.Title}");
                bool ok;
                try
                {
                    ok = await step.Run();
                }
                catch (Exception ex)
                {
                    ok = false;
                    Log(d.Serial, "  ошибка: " + ex.Message);
                }

                d.Done++;
                _devicesGrid.Invalidate();

                if (ok)
                {
                    Log(d.Serial, $"OK    {step.Title}");
                    if (step.Reboot) rebooted = true;
                    continue;
                }

                d.Errors++;
                Log(d.Serial, $"ERR   {step.Title}");
                if (step.Critical)
                {
                    Log(d.Serial, "Остановлено: без этого шага продолжать нельзя");
                    break;
                }
            }
        }
        finally
        {
            EndWork(d);
            d.InfoLoaded = false;
            Log(d.Serial, d.Errors == 0 ? $"{activity}: готово" : $"{activity}: завершено, ошибок {d.Errors}");
        }
        return rebooted;
    }

    #endregion

    #region Verification

    // После прошивки: дождаться, пока планшет загрузится, и проверить его. Проверка повторяется,
    // потому что сразу после загрузки Wi-Fi и FreeKiosk ещё поднимаются
    private async Task VerifyAfterAsync(List<(Device Device, bool Rebooted, bool Failed)> runs)
    {
        var targets = runs.Where(r => !r.Failed || r.Rebooted).ToList();
        if (targets.Count == 0 || StopRequested)
            return;

        Log(SystemSource, $"Автопроверка {targets.Count} планшетов" +
                          (targets.Any(t => t.Rebooted) ? " — жду загрузки после перезагрузки" : ""));
        var results = (await Task.WhenAll(targets.Select(t => VerifyOneAsync(t.Device, t.Rebooted)))).ToList();
        if (StopRequested)
            return;

        var bad = results.Where(r => r.Items.Any(i => !i.Ok)).ToList();
        foreach (var (device, items) in results)
        {
            var failedItems = items.Where(i => !i.Ok).ToList();
            Log(SystemSource, failedItems.Count == 0
                ? $"Проверка {device}: OK"
                : $"Проверка {device}: ошибка — {string.Join("; ", failedItems.Select(i => $"{i.Title}: {i.Details}"))}");
        }

        if (bad.Count == 0)
        {
            Dialogs.Result(this, "Изменения прошли успешно", "Планшеты прошиты и проверены.",
                results.Select(r => new ResultRow(r.Device, "Проверено", true)).ToList());
            return;
        }
        using var dialog = new CheckResultsDialog(results);
        dialog.ShowDialog(this);
    }

    private async Task<(string Device, List<CheckItem> Items)> VerifyOneAsync(Device original, bool rebooted)
    {
        var d = original;
        if (rebooted)
        {
            var back = await WaitForReturnAsync(original, TimeSpan.FromMinutes(4));
            if (back is null)
            {
                var reason = StopRequested ? "остановлено" : "не подключился за 4 минуты после перезагрузки";
                Log(original.Serial, "ERR   Проверка: " + reason);
                return (original.DisplayName, new List<CheckItem> { new("Планшет снова на связи", false, reason) });
            }
            d = back;
        }

        BeginWork(d, "Проверка", 1);
        var items = new List<CheckItem>();
        try
        {
            for (int attempt = 1; attempt <= 4 && !StopRequested; attempt++)
            {
                items = await DeviceChecker.RunAsync(new AdbClient(d.TransportId, null, StopToken), _profile);
                if (items.All(i => i.Ok))
                    break;
                if (attempt < 4)
                    await Task.Delay(TimeSpan.FromSeconds(10));
            }
            d.Errors = items.Count(i => !i.Ok);
            return (d.DisplayName, items);
        }
        catch (Exception ex)
        {
            d.Errors = 1;
            return (d.DisplayName, new List<CheckItem> { new("Связь с планшетом", false, ex.Message) });
        }
        finally
        {
            d.Done = 1;
            EndWork(d);
        }
    }

    // После перезагрузки adb выдаёт планшету новый номер подключения, поэтому ищем его по серийнику
    private async Task<Device?> WaitForReturnAsync(Device original, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var sawDisconnect = false;
        while (DateTime.UtcNow < deadline && !StopRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!_devices.Contains(original) || original.State != "device")
                sawDisconnect = true;
            if (!sawDisconnect)
                continue;

            var candidate = _devices.FirstOrDefault(x => x != original && x.Serial == original.Serial && x.IsReady && !x.IsBusy);
            if (candidate is null)
                continue;
            if (!await DeviceQueries.IsBootCompletedAsync(new AdbClient(candidate.TransportId, null, StopToken)))
                continue;

            Log(candidate.Serial, "Планшет загрузился, проверяю");
            await Task.Delay(TimeSpan.FromSeconds(5));
            return candidate;
        }
        return null;
    }

    #endregion

    #region Painting

    private static (string Text, Color Color) DeviceStatus(Device d) => d.State switch
    {
        "unauthorized" => ("Подтвердите отладку на экране", Theme.WarningText),
        "offline" => ("Офлайн", Theme.WarningText),
        "recovery" or "sideload" => ("Режим восстановления", Theme.WarningText),
        "disconnected" => ("Отключён во время работы", Theme.DangerText),
        "device" when d.IsBusy => ($"{d.Activity} {d.Done}/{d.Total}", Theme.AccentText),
        "device" when !d.InfoLoaded && d.Total == 0 => ("Загружается…", Theme.Muted),
        "device" when d.Total > 0 && d.Activity == "Проверка" && d.Errors == 0 => ("Проверено", Theme.SuccessText),
        "device" when d.Total > 0 && d.Errors > 0 => ($"Готово · ошибок: {d.Errors}", Theme.DangerText),
        "device" when d.Total > 0 => ("Готово", Theme.SuccessText),
        "device" => ("Готов к работе", Theme.Muted),
        _ => (d.State, Theme.Muted)
    };

    private void PaintDeviceCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics is null) return;
        if (_devicesGrid.Rows[e.RowIndex].Tag is not Device d) return;

        var column = _devicesGrid.Columns[e.ColumnIndex].Name;
        if (column == "Check") return;

        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        var g = e.Graphics;
        var r = e.CellBounds;

        if (column == "Device")
        {
            TextRenderer.DrawText(g, d.Title, Theme.BoldFont, new Rectangle(r.X + 4, r.Y + 8, r.Width - 8, 20), Theme.TextColor,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            var parts = new[]
            {
                d.Name.Length > 0 ? d.Model : "",
                d.Serial,
                d.AndroidVersion.Length > 0 ? $"Android {d.AndroidVersion}" : ""
            };
            var sub = string.Join(" · ", parts.Where(s => s.Length > 0));
            TextRenderer.DrawText(g, sub, Theme.SmallFont, new Rectangle(r.X + 4, r.Y + 28, r.Width - 8, 18), Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        else if (column == "Status")
        {
            var (text, color) = DeviceStatus(d);
            TextRenderer.DrawText(g, text, Theme.SmallFont, new Rectangle(r.X + 4, r.Y + 9, r.Width - 12, 18), color,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (d.Total > 0)
            {
                var bar = new Rectangle(r.X + 4, r.Y + 32, r.Width - 16, 6);
                Theme.FillRounded(g, bar, 3, Theme.TrackBack);
                var width = (int)(bar.Width * (d.Done / (double)d.Total));
                if (width >= 6)
                    Theme.FillRounded(g, new Rectangle(bar.X, bar.Y, width, bar.Height), 3, color);
            }
        }
        e.Handled = true;
    }

    private void PaintPackageCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics is null) return;
        if (_pkgGrid.Rows[e.RowIndex].Tag is not PackageRow p) return;

        var column = _pkgGrid.Columns[e.ColumnIndex].Name;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;

        if (column == "Check" && p.IsLocked)
        {
            e.PaintBackground(e.CellBounds, selected);
            Theme.DrawLock(e.Graphics, e.CellBounds, Theme.Muted);
            e.Handled = true;
        }
        else if (column == "Category")
        {
            e.PaintBackground(e.CellBounds, selected);
            var text = Theme.CategoryText(p.Info.Category);
            var (fore, back) = Theme.CategoryColors(p.Info.Category);
            var size = TextRenderer.MeasureText(text, Theme.ChipFont);
            var r = e.CellBounds;
            var chip = new Rectangle(r.X + 6, r.Y + (r.Height - 22) / 2, Math.Min(size.Width + 16, r.Width - 12), 22);
            Theme.FillRounded(e.Graphics, chip, 11, back);
            TextRenderer.DrawText(e.Graphics, text, Theme.ChipFont, chip, fore,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }
    }

    #endregion

    #region Log

    private void Log(string source, string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Log(source, text)); }
            catch (InvalidOperationException) { }
            return;
        }

        var entry = (DateTime.Now, source, text);
        _fileLog.Write(entry.Item1, source, text);
        _logEntries.Add(entry);
        if (_logEntries.Count > MaxLogEntries)
            _logEntries.RemoveRange(0, _logEntries.Count - MaxLogEntries);

        if (MatchesLogFilter(source))
            _log.AppendLine(FormatLog(entry));
    }

    private bool MatchesLogFilter(string source) =>
        _logFilter.SelectedIndex <= 0 || source == _logFilter.SelectedItem as string;

    private static string FormatLog((DateTime Time, string Source, string Text) e) =>
        $"[{e.Time:HH:mm:ss}] {e.Source,-20} {e.Text}";

    private void RebuildLogView() =>
        _log.SetLines(_logEntries.Where(e => MatchesLogFilter(e.Source)).Select(FormatLog));

    #endregion
}
