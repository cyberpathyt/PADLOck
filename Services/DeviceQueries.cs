using System.Text.RegularExpressions;
using PADLOck.Models;

namespace PADLOck.Services;

public sealed record DeviceProps(string AndroidVersion, string Manufacturer, string Name, string DeviceOwner);

public static class DeviceQueries
{
    // Пока Android загружается, системные службы недоступны и команды дают ошибки
    public static async Task<bool> IsBootCompletedAsync(AdbClient adb) =>
        (await adb.ShellAsync("getprop sys.boot_completed", logAs: "").ConfigureAwait(false)).StdOut == "1";

    public static async Task<DeviceProps> ReadInfoAsync(AdbClient adb)
    {
        var android = (await adb.ShellAsync("getprop ro.build.version.release").ConfigureAwait(false)).StdOut;
        var manufacturer = (await adb.ShellAsync("getprop ro.product.manufacturer").ConfigureAwait(false)).StdOut;

        var name = (await adb.ShellAsync("settings get global device_name").ConfigureAwait(false)).StdOut;
        if (name == "null") name = "";

        var owners = await adb.ShellAsync("dpm list-owners").ConfigureAwait(false);
        string owner;
        if (!owners.Success)
            owner = "?";
        else if (owners.StdOut.Contains("no owners", StringComparison.OrdinalIgnoreCase))
            owner = "нет";
        else
        {
            var m = Regex.Match(owners.StdOut, @"admin=([^/\s,]+)");
            owner = m.Success ? m.Groups[1].Value : owners.StdOut;
        }

        return new DeviceProps(android, manufacturer, name, owner);
    }

    // Имя хранится на самом планшете (Настройки → О планшете → Имя устройства)
    public static async Task<bool> SetNameAsync(AdbClient adb, string name)
    {
        AdbResult r;
        if (name.Length == 0)
            r = await adb.ShellAsync("settings delete global device_name").ConfigureAwait(false);
        else
        {
            var escaped = name.Replace("\"", "").Replace("'", "'\\''");
            r = await adb.ShellAsync($"settings put global device_name '{escaped}'").ConfigureAwait(false);
        }
        return r.Success;
    }

    public static async Task<List<(string Name, PackageState State)>> ReadPackagesAsync(AdbClient adb)
    {
        var all = Parse((await adb.ShellAsync("pm list packages -u").ConfigureAwait(false)).StdOut);
        var installed = Parse((await adb.ShellAsync("pm list packages").ConfigureAwait(false)).StdOut);
        var disabled = Parse((await adb.ShellAsync("pm list packages -d").ConfigureAwait(false)).StdOut);

        return all.Select(name =>
        {
            var state = !installed.Contains(name) ? PackageState.Uninstalled
                      : disabled.Contains(name) ? PackageState.Disabled
                      : PackageState.Installed;
            return (name, state);
        }).ToList();
    }

    private static HashSet<string> Parse(string output) =>
        output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
              .Where(l => l.StartsWith("package:"))
              .Select(l => l["package:".Length..])
              .ToHashSet();
}
