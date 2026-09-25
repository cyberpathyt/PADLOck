using PADLOck.Models;

namespace PADLOck.Services;

public enum OutcomeKind { Ok, AlreadyDone, Skipped, Failed }

public sealed record Outcome(OutcomeKind Kind, string Message);

public static class PackageOperations
{
    public static async Task<Outcome> ApplyAsync(AdbClient adb, string package, PackageAction action,
                                                 CatalogEntry info, bool kioskIsOwner)
    {
        if (info.Category == PackageCategory.Critical)
            return new(OutcomeKind.Skipped, "критичный пакет");

        if (info.RequiresKioskHome && action != PackageAction.Restore && !kioskIsOwner)
            return new(OutcomeKind.Skipped, "FreeKiosk ещё не Device Owner");

        switch (action)
        {
            case PackageAction.Remove:
                return Evaluate(await adb.ShellAsync($"pm uninstall -k --user 0 {package}").ConfigureAwait(false),
                                alreadyMarker: "not installed for");

            case PackageAction.Disable:
                return Evaluate(await adb.ShellAsync($"pm disable-user --user 0 {package}").ConfigureAwait(false));

            case PackageAction.Restore:
                var reinstall = Evaluate(await adb.ShellAsync($"cmd package install-existing {package}").ConfigureAwait(false));
                if (reinstall.Kind == OutcomeKind.Failed)
                    return reinstall;
                return Evaluate(await adb.ShellAsync($"pm enable {package}").ConfigureAwait(false));

            default:
                return new(OutcomeKind.Failed, "неизвестное действие");
        }
    }

    public static async Task<Outcome> InstallApkAsync(AdbClient adb, string apkPath)
    {
        // -r: переустановка поверх, -g: выдать все runtime-разрешения
        return Evaluate(await adb.RunAsync($"install -r -g \"{apkPath}\"").ConfigureAwait(false));
    }

    private static Outcome Evaluate(AdbResult r, string? alreadyMarker = null)
    {
        var text = $"{r.StdOut} {r.StdErr}".Trim();

        if (alreadyMarker is not null && text.Contains(alreadyMarker, StringComparison.OrdinalIgnoreCase))
            return new(OutcomeKind.AlreadyDone, text);

        // pm может вернуть код 0 вместе с Failure
        var failed = r.ExitCode != 0
                     || text.Contains("Failure", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                     || text.StartsWith("Error", StringComparison.OrdinalIgnoreCase);

        return new(failed ? OutcomeKind.Failed : OutcomeKind.Ok, text);
    }
}
