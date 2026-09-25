using System.Diagnostics;
using System.Text;

namespace PADLOck.Services;

public sealed record AdbResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class AdbClient
{
    public static string AdbPath { get; } = Path.Combine(AppContext.BaseDirectory, "tools", "adb.exe");
    public static bool AdbExists => File.Exists(AdbPath);

    private readonly string? _transportId;
    private readonly Action<string>? _log;
    private readonly CancellationToken _stop;

    // stop — общая отмена операции (кнопка «Стоп»): запущенная команда прерывается, новые не запускаются
    public AdbClient(string? transportId, Action<string>? log, CancellationToken stop = default)
    {
        _transportId = transportId;
        _log = log;
        _stop = stop;
    }

    // Сколько ждать ответа. Установка APK идёт дольше остальных команд
    public static TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public static TimeSpan InstallTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // logAs — что писать в журнал вместо реальной команды (чтобы не светить пароли)
    public async Task<AdbResult> RunAsync(string arguments, string? logAs = null, CancellationToken ct = default)
    {
        if (_stop.IsCancellationRequested || ct.IsCancellationRequested)
            return new AdbResult(-1, "", "операция отменена");

        var fullArgs = _transportId is null ? arguments : $"-t {_transportId} {arguments}";

        var psi = new ProcessStartInfo(AdbPath, fullArgs)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (logAs != "")
            _log?.Invoke($"> adb {logAs ?? arguments}");

        var timeout = arguments.StartsWith("install", StringComparison.Ordinal) ? InstallTimeout : CommandTimeout;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop);
        limit.CancelAfter(timeout);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Планшет не ответил: отключили кабель, завис и т.п. Процесс adb снимаем, чтобы не копились
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            var reason = ct.IsCancellationRequested || _stop.IsCancellationRequested ? "операция отменена" : $"нет ответа за {timeout.TotalSeconds:0} с";
            _log?.Invoke($"  {reason}");
            return new AdbResult(-1, "", reason);
        }

        var result = new AdbResult(process.ExitCode,
            (await outTask.ConfigureAwait(false)).Trim(),
            (await errTask.ConfigureAwait(false)).Trim());

        if (!result.Success)
            _log?.Invoke($"  код {result.ExitCode}: {result.StdErr} {result.StdOut}".TrimEnd());

        return result;
    }

    public Task<AdbResult> ShellAsync(string command, string? logAs = null, CancellationToken ct = default)
        => RunAsync($"shell {command}", logAs is null or "" ? logAs : $"shell {logAs}", ct);

    // Значение для удалённого sh в одинарных кавычках. Двойные кавычки экранируются
    // для командной строки Windows, иначе adb.exe их не получит.
    public static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''").Replace("\"", "\\\"") + "'";
}
