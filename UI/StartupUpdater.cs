using System.Diagnostics;
using PADLOck.Services;

namespace PADLOck.UI;

// Проверка обновлений при запуске, до главного окна — как в Steam:
// заставка «Проверка обновлений…» → если есть новая версия, скачивание с прогрессом → установка → запуск свежей версии.
// Если обновилась только версия FreeKiosk — скачивается APK, и открывается главное окно.
internal static class StartupUpdater
{
    // Что записать в журнал главного окна, когда оно откроется
    public static List<string> Messages { get; } = new();

    // false — запущен установщик, программу нужно закрыть
    public static bool Run()
    {
        using var splash = new StartupSplash();
        Application.Run(splash);
        return !splash.InstallerStarted;
    }
}

internal sealed class StartupSplash : DarkDialog
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(6);

    private readonly Label _status = new() { AutoSize = false, ForeColor = Theme.TextColor, Font = Theme.BoldFont };
    private readonly Label _details = new() { AutoSize = false, ForeColor = Theme.Muted, Font = Theme.SmallFont };
    private readonly ProgressLine _bar = new() { Visible = false };
    private UpdateService _service = new();

    public StartupSplash()
    {
        Text = "PADLOck";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        BackColor = Theme.BaseBottom;
        ClientSize = new Size(440, 190);
        Font = Theme.UiFont;

        var logo = new PictureBox
        {
            Size = new Size(48, 48),
            Location = new Point(28, 30),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.BaseBottom,
            Image = Forms.AppIcon is { } icon ? new Icon(icon, 48, 48).ToBitmap() : null
        };
        var title = new Label
        {
            Text = "PADLOck", Font = Theme.TitleFont, ForeColor = Color.White, AutoSize = true, Location = new Point(92, 28)
        };
        var version = new Label
        {
            Text = $"версия {AppInfo.Version} · {AppInfo.Author}", Font = Theme.SmallFont, ForeColor = Theme.Dimmed,
            AutoSize = true, Location = new Point(94, 58)
        };
        _status.SetBounds(28, 104, 384, 22);
        _bar.SetBounds(28, 132, 384, 14);
        _details.SetBounds(28, 152, 384, 20);
        _status.Text = "Проверка обновлений…";

        Controls.AddRange(new Control[] { logo, title, version, _status, _bar, _details });
    }

    public bool InstallerStarted { get; private set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(51, 65, 85));
        e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await CheckAsync();
        }
        catch (Exception ex)
        {
            StartupUpdater.Messages.Add("Обновления: " + ex.Message);
        }
        Close();
    }

    private async Task CheckAsync()
    {
        var settings = AppSettings.Load();
        var profile = KioskProfile.Load();
        var channel = settings.UpdateDevChannel ? "dev" : "stable";

        ReleaseInfo? release;
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            _service = new UpdateService(settings.EffectiveUpdateSource);
            _status.Text = $"Проверка обновлений ({UpdateService.SourceName(settings.EffectiveUpdateSource)})…";
            release = await _service.FindLatestAsync(settings.UpdateDevChannel, timeout.Token);
        }
        catch (Exception ex)
        {
            var reason = ex switch
            {
                UpdateException => ex.Message,
                OperationCanceledException => $"{UpdateService.SourceName(settings.EffectiveUpdateSource)} не ответил за 6 секунд",
                _ => $"нет связи ({UpdateService.SourceName(settings.EffectiveUpdateSource)}): " + UpdateService.Describe(ex)
            };
            StartupUpdater.Messages.Add($"Обновления: проверить не удалось — {reason}");
            return;
        }
        if (release is null)
        {
            StartupUpdater.Messages.Add($"Обновления: в канале {channel} ещё нет версий");
            return;
        }

        // Новая версия программы
        var newer = SemVersion.TryParse(AppInfo.Version, out var current) && release.Version.CompareTo(current) > 0;
        if (newer && release.Installer is not null)
        {
            var recentlyFailed = settings.LastUpdateAttempt == release.Tag
                                 && DateTime.UtcNow - settings.LastUpdateAttemptUtc < TimeSpan.FromMinutes(10);
            if (recentlyFailed)
            {
                StartupUpdater.Messages.Add($"Обновления: версия {release.Version} недавно не установилась. " +
                                            "Повторить можно через «Обновления → Проверить обновления»");
            }
            else if (await InstallAsync(release, settings))
            {
                return;
            }
        }

        // FreeKiosk из релиза
        if (release.Apk is not null && profile.ApkFolder.Trim().Length == 0)
            await SyncApkAsync(release);
        else if (release.Apk is not null)
            StartupUpdater.Messages.Add($"Обновления: в релизе {release.Apk.Name}, но в настройках указана своя папка с APK — прошивка идёт из неё");
    }

    private async Task<bool> InstallAsync(ReleaseInfo release, AppSettings settings)
    {
        var installer = release.Installer!;
        _status.Text = $"Загрузка обновления {release.Version}{(release.Prerelease ? " (dev)" : "")}";
        var path = Path.Combine(AppInfo.UpdatesDir, installer.Name);
        try
        {
            CleanDirectory(AppInfo.UpdatesDir, "*", null);
            await DownloadAsync(installer, path);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync("Обновление не скачалось", ex);
            return false;
        }

        settings.LastUpdateAttempt = release.Tag;
        settings.LastUpdateAttemptUtc = DateTime.UtcNow;
        try
        {
            settings.Save();
        }
        catch (IOException)
        {
        }

        _status.Text = $"Установка {release.Version}…";
        _details.Text = "Подтвердите запрос Windows — после установки PADLOck запустится сам";
        _bar.Value = 1;
        try
        {
            // Установщик сам запросит права администратора, покажет ход установки и снова запустит программу
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NOCANCEL")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowFailureAsync("Установка не запустилась", ex);
            return false;
        }
        InstallerStarted = true;
        await Task.Delay(1200);
        return true;
    }

    private async Task SyncApkAsync(ReleaseInfo release)
    {
        var apk = release.Apk!;
        var destination = Path.Combine(AppInfo.ReleaseApkDir, apk.Name);
        if (File.Exists(destination) && apk.Sha256 is { } expected
            && await Task.Run(() => UpdateService.Sha256Of(destination)) == expected)
        {
            CleanDirectory(AppInfo.ReleaseApkDir, "*.apk", destination);
            return;
        }

        _status.Text = $"Загрузка FreeKiosk: {apk.Name}";
        try
        {
            await DownloadAsync(apk, destination);
            CleanDirectory(AppInfo.ReleaseApkDir, "*.apk", destination);
            StartupUpdater.Messages.Add($"Обновления: FreeKiosk {apk.Name} готов к прошивке");
        }
        catch (Exception ex)
        {
            await ShowFailureAsync("FreeKiosk не скачался", ex);
        }
    }

    private async Task DownloadAsync(UpdateAsset asset, string destination)
    {
        _bar.Visible = true;
        _bar.Value = 0;
        var started = DateTime.UtcNow;
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            var fraction = p.Total > 0 ? Math.Min(1.0, p.Done / (double)p.Total) : 0;
            _bar.Value = fraction;
            var seconds = Math.Max(0.5, (DateTime.UtcNow - started).TotalSeconds);
            _details.Text = $"{fraction * 100:0}%  ·  {Mb(p.Done)} из {Mb(p.Total)}  ·  {p.Done / seconds / 1048576:0.0} МБ/с";
        });
        await _service.DownloadAsync(asset, destination, progress, CancellationToken.None);
        _details.Text = "Скачано и проверено";
    }

    private async Task ShowFailureAsync(string what, Exception ex)
    {
        var reason = UpdateService.Describe(ex);
        StartupUpdater.Messages.Add($"Обновления: {what} — {reason}");
        _status.Text = what;
        _status.ForeColor = Theme.DangerText;
        _details.Text = reason;
        await Task.Delay(2500);
    }

    private static void CleanDirectory(string dir, string pattern, string? keep)
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
        }
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} МБ";
}
