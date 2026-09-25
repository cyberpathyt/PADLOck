using System.Text.Json;
using System.Text.RegularExpressions;
using PADLOck.Models;

namespace PADLOck.Services;

public enum StepKey { Check, Install, Owner, Permissions, Wifi, System, Region, Apps, Kiosk, Url, Mqtt, Reboot }

public sealed record ProvisionStep(StepKey Key, string Title, string Hint, bool Critical, Func<Task<bool>> Run, bool Reboot = false);

public sealed class KioskProvisioner
{
    public const string KioskPackage = "com.freekiosk";
    private const string AdminComponent = "com.freekiosk/.DeviceAdminReceiver";
    private const string KioskActivity = "com.freekiosk/.MainActivity";

    // Шаги, без которых киоск не заблокирован: их добавляем непрошитым планшетам
    public static readonly StepKey[] BaseKeys = { StepKey.Check, StepKey.Install, StepKey.Owner, StepKey.Permissions };

    // Не удаляются никогда, что бы ни было в профиле
    public static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        KioskPackage,
        "com.android.settings",
        "com.android.launcher3",
        "com.android.systemui",
        "com.android.shell",
        "com.google.android.inputmethod.latin",
        "com.google.android.webview",
        "com.google.android.documentsui",
    };

    private readonly AdbClient _adb;
    private readonly KioskProfile _profile;
    private readonly string? _apkPath;
    private readonly PackageCatalog _catalog;
    private readonly SemaphoreSlim _installSlots;
    private readonly Action<string> _log;
    private readonly string _deviceName;
    private string _owner = "";

    public KioskProvisioner(AdbClient adb, KioskProfile profile, string? apkPath, PackageCatalog catalog,
                            SemaphoreSlim installSlots, string deviceName, Action<string> log)
    {
        _adb = adb;
        _profile = profile;
        _apkPath = apkPath;
        _catalog = catalog;
        _installSlots = installSlots;
        _deviceName = deviceName;
        _log = log;
    }

    // После добавления сети подождать подключения к ней (кнопка «Изменить Wi-Fi»)
    public bool WaitForWifi { get; init; }

    // Все шаги прошивки в порядке выполнения. Что из них выполнять, выбирается галочками
    public static List<ProvisionStep> Describe(KioskProfile profile) =>
        new KioskProvisioner(new AdbClient(null, null), profile, null, PackageCatalog.Empty, new SemaphoreSlim(1), "", _ => { }).AllSteps();

    public List<ProvisionStep> AllSteps()
    {
        var p = _profile;
        var ssid = p.WifiSsid.Trim().Length == 0 ? "сеть не задана — только настройки Wi-Fi" : $"сеть «{p.WifiSsid.Trim()}»";
        var steps = new List<ProvisionStep>
        {
            new(StepKey.Check, "Проверка планшета", "нет чужого владельца устройства и аккаунтов Google", true, CheckAsync),
            new(StepKey.Install, "Установка FreeKiosk", "APK из релиза программы; если эта версия уже стоит — пропускается", true, InstallAsync),
            new(StepKey.Owner, "Device Owner", "FreeKiosk становится владельцем устройства — без этого киоск не заблокирован", true, DeviceOwnerAsync),
            new(StepKey.Permissions, "Разрешения FreeKiosk", "статистика использования и системные настройки", false, PermissionsAsync),
            new(StepKey.Wifi, "Wi-Fi", ssid + (p.WifiForgetOthers ? ", остальные сети забыть" : ""), false, WifiAsync),
            new(StepKey.System, "Экран блокировки и геолокация",
                (p.DisableLockScreen ? "без экрана блокировки" : "экран блокировки не трогать") +
                (p.DisableLocation ? ", геолокация выключена" : ""), false, SystemAsync),
            new(StepKey.Region, "Часовой пояс, время и язык",
                $"{p.TimeZone}, {p.Language}" + (p.SyncTimeFromPc ? ", время с компьютера" : ""), false, RegionAsync),
            new(StepKey.Apps, "Удаление приложений",
                $"{p.RemovePackages.Count} по списку; уже удалённые пропускаются после проверки", false, RemovePackagesAsync),
        };
        if (p.KioskConfigured)
            steps.Add(new(StepKey.Kiosk, "Настройки киоска",
                          p.KioskMode == "app" ? "приложение " + p.KioskApp.Trim() : p.KioskUrl.Trim(), false, KioskAsync));
        steps.Add(new(StepKey.Mqtt, "Мониторинг (MQTT)",
                      p.MqttEnabled ? $"{p.MqttBroker.Trim()}:{p.MqttPort}" : "выключить отправку статуса", false, MqttAsync));
        steps.Add(new(StepKey.Reboot, "Перезагрузка", "после загрузки планшет проверяется автоматически", false, RebootAsync, Reboot: true));
        return steps;
    }

    // Отдельные действия: смена Wi-Fi и адреса страницы
    public List<ProvisionStep> WifiOnlySteps() => AllSteps().Where(s => s.Key == StepKey.Wifi).ToList();

    public List<ProvisionStep> UrlOnlySteps() => new()
    {
        new(StepKey.Url, "Адрес страницы", _profile.KioskUrl.Trim(), false, UrlAsync)
    };

    // Какие шаги соответствуют изменённым группам настроек
    public static HashSet<StepKey> KeysFor(IEnumerable<SettingsGroup> groups) => groups.Select(g => g switch
    {
        SettingsGroup.Update => StepKey.Install,
        SettingsGroup.Wifi => StepKey.Wifi,
        SettingsGroup.System => StepKey.System,
        SettingsGroup.Region => StepKey.Region,
        SettingsGroup.Apps => StepKey.Apps,
        SettingsGroup.Kiosk => StepKey.Kiosk,
        SettingsGroup.Mqtt => StepKey.Mqtt,
        _ => StepKey.Reboot,
    }).ToHashSet();

    private async Task<bool> CheckAsync()
    {
        _owner = await ReadOwnerAsync(_adb);
        if (_owner.Length > 0 && _owner != KioskPackage)
        {
            _log($"  владелец устройства уже назначен: {_owner}");
            return false;
        }

        if (_owner.Length == 0)
        {
            var accounts = await _adb.ShellAsync("dumpsys account");
            if (Regex.IsMatch(accounts.StdOut, @"Account \{"))
            {
                _log("  на планшете есть аккаунты — Device Owner назначается только без них. Удалите аккаунты в Настройках");
                return false;
            }
        }
        return true;
    }

    public static async Task<string> ReadOwnerAsync(AdbClient adb)
    {
        var owners = await adb.ShellAsync("dpm list-owners");
        if (owners.StdOut.Contains("no owners", StringComparison.OrdinalIgnoreCase))
            return "";
        var m = Regex.Match(owners.StdOut, @"admin=([^/\s,]+)");
        return m.Success ? m.Groups[1].Value : "?";
    }

    private async Task<bool> InstallAsync()
    {
        if (_apkPath is null)
        {
            _log("  нет APK в папке");
            return false;
        }
        _log($"  {Path.GetFileName(_apkPath)}");

        // Повторная прошивка: та же версия уже стоит — не тратим время на установку
        var apkVersion = VersionFromFileName(_apkPath);
        if (apkVersion is not null)
        {
            var installed = await InstalledKioskVersionAsync(_adb);
            if (installed == apkVersion)
            {
                _log($"  версия {installed} уже установлена — пропускаю");
                return true;
            }
            if (installed is not null)
                _log($"  на планшете {installed} → ставлю {apkVersion}");
        }

        await _installSlots.WaitAsync();
        try
        {
            var r = await _adb.RunAsync($"install -r -g \"{_apkPath}\"");
            var ok = r.StdOut.Contains("Success", StringComparison.OrdinalIgnoreCase);
            if (!ok) _log($"  {r.StdOut} {r.StdErr}".TrimEnd());
            return ok;
        }
        finally
        {
            _installSlots.Release();
        }
    }

    // freekiosk-v2.0.0-beta.3.apk → 2.0.0-beta.3
    public static string? VersionFromFileName(string path)
    {
        var m = Regex.Match(Path.GetFileNameWithoutExtension(path), @"v?(\d+(?:\.\d+)+(?:-[0-9A-Za-z.]+)?)$");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static async Task<string?> InstalledKioskVersionAsync(AdbClient adb)
    {
        var dump = (await adb.ShellAsync($"dumpsys package {KioskPackage}", logAs: "")).StdOut;
        var m = Regex.Match(dump, @"versionName=(\S+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private async Task<bool> DeviceOwnerAsync()
    {
        if (_owner == KioskPackage)
        {
            _log("  уже назначен");
            return true;
        }
        var r = await _adb.ShellAsync($"dpm set-device-owner {AdminComponent}");
        var ok = r.StdOut.Contains("Success", StringComparison.OrdinalIgnoreCase);
        if (!ok) _log($"  {r.StdOut} {r.StdErr}".TrimEnd());
        return ok;
    }

    private async Task<bool> PermissionsAsync()
    {
        var ok = await Shell($"appops set {KioskPackage} android:get_usage_stats allow");
        ok &= await Shell($"pm grant {KioskPackage} android.permission.WRITE_SECURE_SETTINGS");
        return ok;
    }

    private async Task<bool> WifiAsync()
    {
        // Сеть без интернета считается рабочей: Android не ищет другую и не помечает её «без доступа»
        var ok = await Shell("settings put global captive_portal_mode 0");
        ok &= await Shell("cmd wifi set-wifi-enabled enabled");

        var ssid = _profile.WifiSsid.Trim();
        if (ssid.Length == 0)
            return ok;

        var security = _profile.WifiSecurity;
        var command = $"cmd wifi add-network {AdbClient.Quote(ssid)} {security}";
        var logged = command;
        if (security is not ("open" or "owe"))
        {
            command += " " + AdbClient.Quote(_profile.WifiPassword);
            logged += " '***'";
        }
        ok &= await Shell(command, logged);

        // Старые сети удаляются только после того, как новая добавлена
        if (ok && _profile.WifiForgetOthers)
        {
            var list = await _adb.ShellAsync("cmd wifi list-networks");
            foreach (var line in list.StdOut.Split('\n').Skip(1))
            {
                var columns = Regex.Split(line.Trim(), @"\s{2,}");
                if (columns.Length < 2 || !int.TryParse(columns[0], out var id) || columns[1] == ssid)
                    continue;
                _log($"  забыть сеть «{columns[1]}»");
                await Shell($"cmd wifi forget-network {id}");
            }
        }

        if (ok && WaitForWifi)
            await WaitWifiConnectedAsync(ssid);
        return ok;
    }

    // Сеть может быть вне зоны (планшеты готовят для другого места), поэтому это не ошибка
    private async Task WaitWifiConnectedAsync(string ssid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            var status = (await _adb.ShellAsync("cmd wifi status", logAs: "")).StdOut;
            var m = Regex.Match(status, "connected to \"(.*?)\"");
            if (m.Success && m.Groups[1].Value == ssid)
            {
                _log($"  подключён к «{ssid}»");
                return;
            }
        }
        _log($"  к «{ssid}» пока не подключился — сеть сохранена, планшет подключится, когда она будет в зоне");
    }

    private async Task<bool> SystemAsync()
    {
        var ok = await Shell($"locksettings set-disabled {(_profile.DisableLockScreen ? "true" : "false")}");
        if (_profile.DisableLocation)
        {
            ok &= await Shell("cmd location set-location-enabled false");
            ok &= await Shell("settings put global wifi_scan_always_enabled 0");
            ok &= await Shell("settings put global ble_scan_always_enabled 0");
        }
        else
        {
            ok &= await Shell("cmd location set-location-enabled true");
        }
        return ok;
    }

    private async Task<bool> RegionAsync()
    {
        var ok = true;
        var zone = _profile.TimeZone.Trim();
        await Shell("cmd time_zone_detector set_auto_detection_enabled false");
        await Shell($"cmd alarm set-timezone {AdbClient.Quote(zone)}");
        var actual = (await _adb.ShellAsync("getprop persist.sys.timezone")).StdOut;
        if (actual != zone)
        {
            _log($"  пояс на планшете: {actual}, ожидался {zone}");
            ok = false;
        }

        if (_profile.NtpServer.Trim().Length > 0)
            ok &= await Shell($"settings put global ntp_server {AdbClient.Quote(_profile.NtpServer.Trim())}");

        if (_profile.SyncTimeFromPc)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!await Shell($"cmd alarm set-time {now}"))
                _log("  время с компьютера не установлено: прошивка не поддерживает команду");
        }

        var locale = AdbClient.Quote(_profile.Language.Trim());
        ok &= await Shell($"cmd locale set-app-locales {KioskPackage} --locales {locale}");
        ok &= await Shell($"cmd locale set-app-locales com.android.settings --locales {locale}");
        return ok;
    }

    private async Task<bool> RemovePackagesAsync()
    {
        int removed = 0, skipped = 0, failed = 0;
        var targets = new List<string>();
        foreach (var raw in _profile.RemovePackages)
        {
            var package = raw.Trim();
            if (package.Length == 0 || targets.Contains(package))
                continue;
            if (Protected.Contains(package) || _catalog.Get(package).Category == PackageCategory.Critical)
            {
                skipped++;
                _log($"  SKIP  {package}: защищённый пакет");
                continue;
            }
            targets.Add(package);
        }

        // Повторная прошивка: одним запросом узнаём, что ещё стоит, и удаляем только это
        var installed = await ListInstalledAsync();
        var absent = 0;
        List<string> toRemove;
        if (installed is null)
        {
            _log("  список пакетов не получен — удаляю по одному");
            toRemove = targets;
        }
        else
        {
            toRemove = targets.Where(installed.Contains).ToList();
            absent = targets.Count - toRemove.Count;
            if (absent > 0)
                _log($"  уже удалены: {absent} из {targets.Count} — пропускаю");
        }

        foreach (var package in toRemove)
        {
            var r = await _adb.ShellAsync($"pm uninstall -k --user 0 {package}");
            var text = $"{r.StdOut} {r.StdErr}".Trim();
            if (text.Contains("Success", StringComparison.OrdinalIgnoreCase))
                removed++;
            else if (text.Contains("not installed", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("Unknown package", StringComparison.OrdinalIgnoreCase))
                absent++;
            else
            {
                failed++;
                _log($"  ERR   {package}: {text}");
            }
        }

        // Контроль: после удаления ни одного пакета из списка остаться не должно
        var after = await ListInstalledAsync();
        if (after is null)
        {
            _log("  контрольный список пакетов не получен");
            failed++;
        }
        else
        {
            var left = targets.Where(after.Contains).ToList();
            if (left.Count > 0)
            {
                _log($"  ERR   остались на планшете: {string.Join(", ", left)}");
                failed = Math.Max(failed, left.Count);
            }
        }

        _log($"  удалено {removed}, уже не было {absent}, защищённых {skipped}, ошибок {failed}");
        return failed == 0;
    }

    // Пакеты, установленные для пользователя 0. null — ответ не похож на полный список, доверять ему нельзя
    private async Task<HashSet<string>?> ListInstalledAsync()
    {
        var r = await _adb.ShellAsync("pm list packages --user 0");
        if (!r.Success)
            return null;
        var set = r.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        return set.Count >= 10 && set.Contains("android") ? set : null;
    }

    private async Task<bool> KioskAsync()
    {
        if (!_profile.KioskConfigured)
        {
            _log("  не задан адрес страницы или приложение");
            return false;
        }

        if (_profile.KioskMode == "app")
        {
            var path = await _adb.ShellAsync($"pm path {_profile.KioskApp.Trim()}");
            if (!path.StdOut.StartsWith("package:"))
            {
                _log($"  приложение {_profile.KioskApp} не установлено на планшете");
                return false;
            }
        }

        var json = _profile.KioskJson();
        var extra = _profile.KioskMode == "app" ? " --ez auto_start true" : "";
        var ok = await SendConfigAsync(json, json, extra);
        ok &= await Shell($"cmd package set-home-activity {KioskActivity}");
        return ok;
    }

    // Только адрес страницы: остальные настройки киоска не трогаются
    private async Task<bool> UrlAsync()
    {
        var url = _profile.KioskUrl.Trim();
        if (url.Length == 0)
        {
            _log("  адрес не задан");
            return false;
        }
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["display_mode"] = "webview", ["url"] = url }, KioskProfile.ConfigJsonOptions);
        return await SendConfigAsync(json, json, "");
    }

    private Task<bool> MqttAsync() =>
        SendConfigAsync(_profile.MqttJson(_deviceName, forLog: false), _profile.MqttJson(_deviceName, forLog: true), "");

    // FreeKiosk принимает настройки только с PIN: на чистом планшете он задаётся, на настроенном должен совпадать
    private async Task<bool> SendConfigAsync(string json, string jsonForLog, string extra)
    {
        var pin = _profile.KioskPin.Trim();
        if (pin.Length == 0)
        {
            _log("  не задан PIN — без него FreeKiosk не примет настройки");
            return false;
        }

        await _adb.ShellAsync("logcat -c", logAs: "");
        var sent = await Shell(
            $"am start -n {KioskActivity} --es pin {AdbClient.Quote(pin)} --es config {AdbClient.Quote(json)}{extra}",
            $"am start -n {KioskActivity} --es pin '***' --es config '{jsonForLog}'{extra}");
        if (!sent)
            return false;

        // Команда возвращается сразу, результат FreeKiosk пишет в logcat с тегом FreeKiosk-ADB:
        // «Config applied» — настройки сохранены, «SETTINGS_LOADED» — FreeKiosk перезапустился и загрузил их
        var saved = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            var log = (await _adb.ShellAsync("logcat -d -s FreeKiosk-ADB:*", logAs: "")).StdOut;

            if (Regex.IsMatch(log, @"invalid pin|wrong pin|pin required|incorrect pin|pin mismatch", RegexOptions.IgnoreCase))
            {
                _log("  PIN не совпадает с PIN киоска на этом планшете — настройки не приняты");
                return false;
            }
            if (log.Contains("SETTINGS_LOADED", StringComparison.OrdinalIgnoreCase))
            {
                _log("  FreeKiosk применил настройки");
                return true;
            }
            if (!saved && log.Contains("Config applied", StringComparison.OrdinalIgnoreCase))
            {
                saved = true;
                // Дальше ждём перезапуска FreeKiosk ещё несколько секунд
                deadline = DateTime.UtcNow.AddSeconds(6);
            }
        }

        if (!saved)
        {
            _log("  FreeKiosk не подтвердил получение настроек — проверьте планшет");
            return false;
        }

        // Настройки сохранены как ожидающие и загрузятся при следующем запуске FreeKiosk — перезапускаем его сами
        _log("  настройки сохранены, перезапускаю FreeKiosk");
        await _adb.ShellAsync($"am force-stop {KioskPackage}");
        await _adb.ShellAsync($"am start -n {KioskActivity}");
        return true;
    }

    private async Task<bool> RebootAsync()
    {
        await _adb.RunAsync("reboot");
        return true;
    }

    private async Task<bool> Shell(string command, string? logAs = null)
    {
        var r = await _adb.ShellAsync(command, logAs);
        var text = $"{r.StdOut} {r.StdErr}".Trim();
        var ok = r.Success
                 && !text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                 && !text.Contains("Failure", StringComparison.OrdinalIgnoreCase)
                 && !text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                 && !text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase);
        if (!ok)
            _log($"  не выполнено: {logAs ?? command} → {text}");
        return ok;
    }
}
