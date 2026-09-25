using PADLOck.Services;

namespace PADLOck;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        AppInfo.EnsureDirectories();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ReportCrash(ex);
        };

        ApplicationConfiguration.Initialize();
        UI.DarkChrome.Initialize();

        // Сначала проверка обновлений (заставка). Если запущена установка новой версии — выходим, она запустится сама
        if (!UI.StartupUpdater.Run())
            return;
        Application.Run(new MainForm());
    }

    private static void ReportCrash(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppInfo.LogsDir, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {AppInfo.Name} {AppInfo.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        MessageBox.Show($"Непредвиденная ошибка:\n{ex.Message}\n\nПодробности записаны в {AppInfo.LogsDir}\\crash.log",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
