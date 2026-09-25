using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PADLOck.Services;

public enum SettingsGroup { Update, Wifi, System, Region, Apps, Kiosk, Mqtt, Reboot }

public sealed class KioskProfile
{
    // Киоск
    public string ApkFolder { get; set; } = "";
    public string KioskMode { get; set; } = "web";          // web | app
    public string KioskUrl { get; set; } = "";
    public string KioskApp { get; set; } = "";
    [JsonIgnore]
    public string KioskPin { get; set; } = "";

    // В файле хранится зашифрованным (DPAPI)
    [JsonPropertyName("KioskPin")]
    public string KioskPinStored
    {
        get => SecretProtector.Protect(KioskPin);
        set => KioskPin = Unprotect(value);
    }
    public string BackButtonMode { get; set; } = "immediate"; // immediate | timer | test
    public bool AllowPowerButton { get; set; }
    public bool StatusBarEnabled { get; set; }
    public bool StatusBarBattery { get; set; } = true;
    public bool StatusBarWifi { get; set; } = true;
    public bool StatusBarTime { get; set; } = true;
    public bool ScreensaverEnabled { get; set; }
    public int ScreensaverDelayMinutes { get; set; } = 5;
    public int ScreensaverBrightness { get; set; } = 10;
    public bool BrightnessManaged { get; set; }
    public int DefaultBrightness { get; set; } = 75;

    // Wi-Fi
    public string WifiSsid { get; set; } = "";
    [JsonIgnore]
    public string WifiPassword { get; set; } = "";

    // В файле хранится зашифрованным (DPAPI)
    [JsonPropertyName("WifiPassword")]
    public string WifiPasswordStored
    {
        get => SecretProtector.Protect(WifiPassword);
        set => WifiPassword = Unprotect(value);
    }
    public string WifiSecurity { get; set; } = "wpa2";
    public bool WifiForgetOthers { get; set; } = true;

    // Регион и время
    public string TimeZone { get; set; } = "Europe/Moscow";
    public string Language { get; set; } = "ru-RU";
    public string NtpServer { get; set; } = "";
    public bool SyncTimeFromPc { get; set; } = true;

    // Система
    public bool DisableLockScreen { get; set; } = true;
    public bool DisableLocation { get; set; } = true;

    // Мониторинг
    public bool MqttEnabled { get; set; }
    public string MqttBroker { get; set; } = "";
    public int MqttPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = "";
    [JsonIgnore]
    public string MqttPassword { get; set; } = "";

    // В файле хранится зашифрованным (DPAPI)
    [JsonPropertyName("MqttPassword")]
    public string MqttPasswordStored
    {
        get => SecretProtector.Protect(MqttPassword);
        set => MqttPassword = Unprotect(value);
    }
    public string MqttBaseTopic { get; set; } = "freekiosk";
    public int MqttStatusInterval { get; set; } = 30;
    public bool MqttAllowControl { get; set; } = true;

    // Приложения
    public List<string> RemovePackages { get; set; } = DefaultRemovePackages();
    public bool RebootWhenDone { get; set; } = true;

    // Пароли сохранены другим пользователем Windows или на другом компьютере — их нужно ввести заново
    [JsonIgnore]
    public bool SecretsUnreadable { get; private set; }

    private string Unprotect(string stored)
    {
        if (SecretProtector.TryUnprotect(stored, out var plain))
            return plain;
        SecretsUnreadable = true;
        return "";
    }

    [JsonIgnore]
    public static string FilePath => Path.Combine(AppInfo.DataDir, "kiosk-profile.json");

    // Профиль по умолчанию, который можно положить рядом с программой при сборке установщика
    [JsonIgnore]
    public static string DefaultFilePath => Path.Combine(AppInfo.ProgramDir, "kiosk-profile.default.json");

    [JsonIgnore]
    public bool KioskConfigured => KioskMode == "app" ? KioskApp.Trim().Length > 0 : KioskUrl.Trim().Length > 0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Для ADB-конфигурации FreeKiosk: без \u0026 вместо & в адресах, чтобы журнал читался
    public static readonly JsonSerializerOptions ConfigJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static KioskProfile Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : File.Exists(DefaultFilePath) ? DefaultFilePath : null;
            if (path is not null)
                return JsonSerializer.Deserialize<KioskProfile>(File.ReadAllText(path), Options) ?? new KioskProfile();
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        return new KioskProfile();
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));

    public KioskProfile Clone() =>
        JsonSerializer.Deserialize<KioskProfile>(JsonSerializer.Serialize(this, Options), Options)!;

    public void CopyFrom(KioskProfile other)
    {
        foreach (var property in typeof(KioskProfile).GetProperties()
                     .Where(p => p.CanWrite && p.GetSetMethod() is not null && !p.Name.EndsWith("Stored")))
            property.SetValue(this, property.GetValue(other));
        RemovePackages = other.RemovePackages.ToList();
    }

    // Какие группы настроек отличаются — чтобы предложить применить именно их
    public HashSet<SettingsGroup> ChangedGroups(KioskProfile before)
    {
        var changed = new HashSet<SettingsGroup>();
        if (WifiSsid != before.WifiSsid || WifiPassword != before.WifiPassword || WifiSecurity != before.WifiSecurity)
            changed.Add(SettingsGroup.Wifi);
        if (TimeZone != before.TimeZone || Language != before.Language || NtpServer != before.NtpServer)
            changed.Add(SettingsGroup.Region);
        if (DisableLockScreen != before.DisableLockScreen || DisableLocation != before.DisableLocation)
            changed.Add(SettingsGroup.System);
        if (!RemovePackages.SequenceEqual(before.RemovePackages))
            changed.Add(SettingsGroup.Apps);
        if (KioskJson() != before.KioskJson())
            changed.Add(SettingsGroup.Kiosk);
        if (MqttJson("x", false) != before.MqttJson("x", false))
            changed.Add(SettingsGroup.Mqtt);
        return changed;
    }

    // Настройки FreeKiosk в формате его ADB-конфигурации (--es config)
    public string KioskJson()
    {
        var c = new Dictionary<string, string>
        {
            ["kiosk_enabled"] = "true",
            ["auto_launch"] = "true",
            ["auto_relaunch"] = "true",
            ["back_button_mode"] = BackButtonMode,
            ["allow_power_button"] = Bool(AllowPowerButton),
            ["status_bar_enabled"] = Bool(StatusBarEnabled),
            ["status_bar_show_battery"] = Bool(StatusBarBattery),
            ["status_bar_show_wifi"] = Bool(StatusBarWifi),
            ["status_bar_show_time"] = Bool(StatusBarTime),
            ["screensaver_enabled"] = Bool(ScreensaverEnabled),
            ["screensaver_delay"] = (Math.Max(1, ScreensaverDelayMinutes) * 60000).ToString(),
            ["screensaver_brightness"] = Math.Clamp(ScreensaverBrightness, 0, 100).ToString(),
            ["brightness_management_enabled"] = Bool(BrightnessManaged),
            ["default_brightness"] = Math.Clamp(DefaultBrightness, 0, 100).ToString(),
        };
        if (KioskMode == "app")
        {
            c["display_mode"] = "external_app";
            c["lock_package"] = KioskApp.Trim();
        }
        else
        {
            c["display_mode"] = "webview";
            c["url"] = KioskUrl.Trim();
        }
        return JsonSerializer.Serialize(c, ConfigJsonOptions);
    }

    public string MqttJson(string deviceName, bool forLog)
    {
        var c = new Dictionary<string, string> { ["mqtt_enabled"] = Bool(MqttEnabled) };
        if (MqttEnabled)
        {
            c["mqtt_broker_url"] = MqttBroker.Trim();
            c["mqtt_port"] = MqttPort.ToString();
            c["mqtt_username"] = MqttUsername.Trim();
            c["mqtt_password"] = forLog && MqttPassword.Length > 0 ? "***" : MqttPassword;
            c["mqtt_base_topic"] = MqttBaseTopic.Trim();
            c["mqtt_status_interval"] = Math.Max(5, MqttStatusInterval).ToString();
            c["mqtt_allow_control"] = Bool(MqttAllowControl);
            c["mqtt_device_name"] = deviceName;
        }
        return JsonSerializer.Serialize(c, ConfigJsonOptions);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    // APK для прошивки: из папки, явно указанной в настройках, иначе — тот, что пришёл с релизом программы
    public string? FindNewestApk()
    {
        var dir = ApkFolder.Trim().Length > 0 ? ApkFolder.Trim() : AppInfo.ReleaseApkDir;
        return Directory.Exists(dir)
            ? new DirectoryInfo(dir).GetFiles("*.apk").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
            : null;
    }

    // Проверено на Teclast T60 Ai: видимые приложения, обновления прошивки и предустановленный набор
    public static List<string> DefaultRemovePackages() => new()
    {
        "com.android.chrome", "com.android.vending", "com.android.calculator2", "com.android.soundrecorder",
        "com.google.android.apps.books", "com.google.android.apps.docs", "com.google.android.apps.kids.home",
        "com.google.android.apps.maps", "com.google.android.apps.photos", "com.google.android.apps.tachyon",
        "com.google.android.apps.youtube.music", "com.google.android.apps.youtube.kids", "com.google.android.youtube",
        "com.google.android.calendar", "com.google.android.contacts", "com.google.android.deskclock",
        "com.google.android.gm", "com.google.android.play.games", "com.google.android.videos", "com.google.android.keep",
        "com.google.android.apps.adm", "com.google.android.apps.googleassistant", "com.google.android.apps.nbu.files",
        "com.google.android.apps.safetyhub", "com.google.android.googlequicksearchbox",
        "com.softwinner.aiocr", "com.softwinner.camera2", "com.softwinner.miracastReceiver", "com.softwinner.videoplayer",
        "com.softwinner.update", "com.teclast.update", "com.teclast.custom", "com.teclast.teclastuserfeedback",
        "com.kms.free", "com.ncloudtech.cloudoffice", "com.vk.im", "com.vkontakte.android", "com.yandex.browser",
        "com.yandex.searchapp", "ru.crptech.mark", "ru.dublgis.dgismobile", "ru.litres.android", "ru.mail.mailapp",
        "ru.mail.search.electroscope", "ru.nspk.mirpay", "ru.ok.android", "ru.rostel", "ru.rutube.app", "ru.vk.store",
        "ru.yandex.disk", "ru.yandex.yandexmaps", "ru.zen.android"
    };
}
