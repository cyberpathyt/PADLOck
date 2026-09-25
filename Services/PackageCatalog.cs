using System.Text.Encodings.Web;
using System.Text.Json;
using PADLOck.Models;

namespace PADLOck.Services;

public sealed class PackageCatalog
{
    private readonly Dictionary<string, CatalogEntry> _entries;

    private PackageCatalog(Dictionary<string, CatalogEntry> entries, int files)
    {
        _entries = entries;
        FileCount = files;
    }

    public static PackageCatalog Empty { get; } = new(new Dictionary<string, CatalogEntry>(), 0);

    public int Count => _entries.Count;
    public int FileCount { get; }

    public CatalogEntry Get(string package) =>
        _entries.TryGetValue(package, out var entry) ? entry : CatalogEntry.Unknown;

    // Загружаются все *.json из папки. Имена пакетов у разных производителей не пересекаются,
    // поэтому один каталог подходит для любого планшета. Пользовательские профили загружаются после встроенных и имеют приоритет.
    public static PackageCatalog LoadFolders(IEnumerable<string> folders, Action<string>? onError)
    {
        var result = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        var files = 0;

        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    LoadFile(file, result);
                    files++;
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        return new PackageCatalog(result, files);
    }

    public static void WriteTemplate(string path, string device, IEnumerable<string> packages)
    {
        var doc = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["device"] = device,
            ["packages"] = packages.OrderBy(p => p).ToDictionary(p => p, _ => new
            {
                title = "",
                category = "unknown",
                description = ""
            })
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, options));
    }

    private static void LoadFile(string path, Dictionary<string, CatalogEntry> target)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), options);

        foreach (var p in doc.RootElement.GetProperty("packages").EnumerateObject())
        {
            var v = p.Value;
            var category = ParseCategory(Str(v, "category"));
            var description = Str(v, "description");
            target[p.Name] = new CatalogEntry(
                Str(v, "title"),
                description.Length > 0 ? description : CatalogEntry.Unknown.Description,
                category,
                v.TryGetProperty("requiresKioskHome", out var rk) && rk.ValueKind == JsonValueKind.True);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static PackageCategory ParseCategory(string s) => s.ToLowerInvariant() switch
    {
        "critical" => PackageCategory.Critical,
        "remove" => PackageCategory.Remove,
        "optional" => PackageCategory.Optional,
        "keep" => PackageCategory.Keep,
        _ => PackageCategory.Unknown
    };
}
