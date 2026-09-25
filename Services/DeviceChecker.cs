using System.Text.RegularExpressions;

namespace PADLOck.Services;

public sealed record CheckItem(string Title, bool Ok, string Details);

public static class DeviceChecker
{
    public static async Task<List<CheckItem>> RunAsync(AdbClient adb, KioskProfile profile)
    {
        var items = new List<CheckItem>();

        var package = (await adb.ShellAsync($"dumpsys package {KioskProvisioner.KioskPackage}")).StdOut;
        var version = Regex.Match(package, @"versionName=(\S+)");
        items.Add(new("FreeKiosk установлен", version.Success, version.Success ? version.Groups[1].Value : "не найден"));

        var owner = await KioskProvisioner.ReadOwnerAsync(adb);
        items.Add(new("Device Owner", owner == KioskProvisioner.KioskPackage, owner.Length == 0 ? "не назначен" : owner));

        if (profile.DisableLockScreen)
        {
            var lockDisabled = (await adb.ShellAsync("locksettings get-disabled")).StdOut;
            items.Add(new("Нет экрана блокировки", lockDisabled == "true", lockDisabled));
        }

        var wifi = (await adb.ShellAsync("cmd wifi status")).StdOut;
        var connected = Regex.Match(wifi, "connected to \"(.*?)\"");
        var expected = profile.WifiSsid.Trim();
        var wifiOk = connected.Success && (expected.Length == 0 || connected.Groups[1].Value == expected);
        items.Add(new("Wi-Fi подключён", wifiOk, connected.Success ? connected.Groups[1].Value : "нет подключения"));

        var captive = (await adb.ShellAsync("settings get global captive_portal_mode")).StdOut;
        items.Add(new("Сеть без интернета не отключается", captive == "0", captive == "0" ? "проверка отключена" : "проверка включена"));

        if (profile.DisableLocation)
        {
            var location = (await adb.ShellAsync("cmd location is-location-enabled")).StdOut;
            items.Add(new("Геолокация выключена", location == "false", location));
        }

        var zone = (await adb.ShellAsync("getprop persist.sys.timezone")).StdOut;
        items.Add(new("Часовой пояс", zone == profile.TimeZone.Trim(), zone));

        var installed = (await adb.ShellAsync("pm list packages")).StdOut
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .ToHashSet();
        var left = profile.RemovePackages
            .Where(p => installed.Contains(p) && !KioskProvisioner.Protected.Contains(p))
            .ToList();
        items.Add(new("Лишние приложения удалены", left.Count == 0,
                      left.Count == 0 ? "всё удалено" : $"осталось {left.Count}: {string.Join(", ", left.Take(5))}{(left.Count > 5 ? "…" : "")}"));

        return items;
    }
}
