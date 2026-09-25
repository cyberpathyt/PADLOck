using System.Text.Json;

namespace PADLOck.Services;

public sealed class AppSettings
{
    public bool AdvancedMode { get; set; }
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // Обновления
    public bool UpdateDevChannel { get; set; }
    public string UpdateSource { get; set; } = ""; // server | github; пусто — сервер, если он задан

    [System.Text.Json.Serialization.JsonIgnore]
    public Services.UpdateSource EffectiveUpdateSource =>
        UpdateSource == "github" || (UpdateSource != "server" && !AppInfo.HasUpdateServer)
            ? Services.UpdateSource.GitHub
            : Services.UpdateSource.Server;
    public string LastUpdateAttempt { get; set; } = "";
    public DateTime LastUpdateAttemptUtc { get; set; }

    private static string FilePath => Path.Combine(AppInfo.DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        return new AppSettings();
    }

    public void Save() =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
