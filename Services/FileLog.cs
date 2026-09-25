using System.Text;

namespace PADLOck.Services;

// Журнал в файлы logs\yyyy-MM-dd.log, хранится 30 дней
public sealed class FileLog : IDisposable
{
    private const int KeepDays = 30;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private DateTime _day;

    public FileLog()
    {
        try
        {
            foreach (var file in Directory.GetFiles(AppInfo.LogsDir, "*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays))
                    File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Write(DateTime time, string source, string text)
    {
        lock (_sync)
        {
            try
            {
                if (_writer is null || time.Date != _day)
                {
                    _writer?.Dispose();
                    _day = time.Date;
                    var path = Path.Combine(AppInfo.LogsDir, $"{_day:yyyy-MM-dd}.log");
                    _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                }
                _writer.WriteLine($"{time:yyyy-MM-dd HH:mm:ss}  {source,-20} {text}");
            }
            catch (IOException)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
