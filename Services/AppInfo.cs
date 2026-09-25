using System.Reflection;

namespace PADLOck.Services;

public static class AppInfo
{
    public const string Name = "PADLOck";
    public const string Author = "vvedyaev";

    // Репозиторий GitHub, из релизов которого берутся обновления программы и APK FreeKiosk
    public const string UpdateRepository = "cyberpathyt/PADLOck";
    public const string UpdateBranch = "main";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "1.0.0";

    // Программа ставится в Program Files, где писать нельзя, поэтому все данные пользователя — в %APPDATA%
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    public static string ProgramDir => AppContext.BaseDirectory;
    public static string LogsDir => Path.Combine(DataDir, "logs");
    // APK FreeKiosk из релиза: здесь всегда ровно один файл, им и прошиваются планшеты
    public static string ReleaseApkDir => Path.Combine(DataDir, "kiosk-release");
    public static string UpdatesDir => Path.Combine(DataDir, "updates");
    public static string UserProfilesDir => Path.Combine(DataDir, "profiles");
    public static string ProgramProfilesDir => Path.Combine(ProgramDir, "profiles");

    public static void EnsureDirectories()
    {
        // Раньше программа называлась KioskPrep — переносим её настройки и журналы
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KioskPrep");
        if (Directory.Exists(legacy) && !Directory.Exists(DataDir))
        {
            try
            {
                CopyDirectory(legacy, DataDir);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var dir in new[] { DataDir, LogsDir, UserProfilesDir, ReleaseApkDir })
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
