using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PADLOck.Services;

public enum UpdateSource { Server, GitHub }

public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(SemVersion Version, string Tag, bool Prerelease, string Notes, string PageUrl,
                                 UpdateAsset? Installer, UpdateAsset? Apk);

// Обновления программы и APK FreeKiosk. Два источника с одинаковым форматом описания версии:
//  • сервер PADLOck Hub во внутренней сети: https://<сервер>/updates/stable.json и dev.json;
//  • GitHub: updates/*.json в репозитории (raw.githubusercontent.com) и файлы релиза.
//    С рабочих компьютеров *.githubusercontent.com бывает недоступен, поэтому по умолчанию — сервер.
// API GitHub не используется: без входа он даёт 60 запросов в час на внешний адрес всего офиса.
// dev.json всегда описывает самую новую версию из обоих каналов.
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    // Адреса можно подменить (для проверки на тестовом сервере)
    public static string RawBase { get; set; } = "https://raw.githubusercontent.com";
    public static string? ServerBase { get; set; } = AppInfo.UpdateServerUrl;
    public static X509Certificate2? ServerCa { get; set; } = AppInfo.LoadUpdateServerCa();

    private readonly UpdateSource _source;

    public UpdateService(UpdateSource source = UpdateSource.GitHub) => _source = source;

    public static string SourceName(UpdateSource s) => s == UpdateSource.Server ? "сервер PADLOck Hub" : "GitHub";

    private static HttpClient CreateClient()
    {
        // Корпоративный прокси с авторизацией Windows
        HttpClient.DefaultProxy.Credentials = CredentialCache.DefaultCredentials;
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = false,
            // Сертификат нашего сервера выпущен его собственным центром сертификации (update-ca.crt рядом с программой).
            // Ему доверяем только для этого центра; остальным сайтам — как обычно, по сертификатам Windows
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (cert is null || ServerCa is null || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                    return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(ServerCa);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(cert);
            }
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version.Replace('+', '.')));
        return client;
    }

    // Последняя версия канала. null — ещё не было ни одного релиза
    public async Task<ReleaseInfo?> FindLatestAsync(bool dev, CancellationToken ct)
    {
        var channel = dev ? "dev" : "stable";
        string url;
        if (_source == UpdateSource.Server)
        {
            if (string.IsNullOrWhiteSpace(ServerBase))
                throw new UpdateException("адрес сервера обновлений не задан (update-server.txt) — выберите GitHub в меню «Обновления»");
            url = $"{ServerBase.TrimEnd('/')}/updates/{channel}.json";
        }
        else
        {
            // Параметр t — чтобы не получить устаревшую копию из кеша
            url = $"{RawBase}/{AppInfo.UpdateRepository}/{AppInfo.UpdateBranch}/updates/{channel}.json?t={DateTime.UtcNow.Ticks}";
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        using var response = await Http.GetAsync(url, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new UpdateException($"{SourceName(_source)} ответил {(int)response.StatusCode} {response.ReasonPhrase}");

        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        try
        {
            return ParseManifest(json, new Uri(url));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UpdateException($"описание версии updates/{channel}.json повреждено");
        }
    }

    // Ссылки на файлы могут быть относительными (files/…) — тогда от адреса самого описания
    public static ReleaseInfo ParseManifest(string json, Uri? baseUri = null)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var tag = r.GetProperty("tag").GetString() ?? "";
        if (!SemVersion.TryParse(r.GetProperty("version").GetString(), out var version))
            throw new FormatException("version");
        return new ReleaseInfo(version, tag,
            r.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
            r.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
            r.TryGetProperty("page", out var page) ? page.GetString() ?? "" : "",
            Asset(r, "installer", baseUri), Asset(r, "apk", baseUri));
    }

    private static UpdateAsset? Asset(JsonElement root, string name, Uri? baseUri)
    {
        if (!root.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Object)
            return null;
        var sha = a.TryGetProperty("sha256", out var s) ? s.GetString()?.Trim().ToLowerInvariant() : null;
        var url = a.GetProperty("url").GetString() ?? "";
        if (baseUri is not null && Uri.TryCreate(baseUri, url, out var absolute))
            url = absolute.ToString();
        return new UpdateAsset(a.GetProperty("name").GetString() ?? "", url,
                               a.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                               sha is { Length: 64 } ? sha : null);
    }

    // Формат sha256sum / Get-FileHash: «<hex>  <имя>» или «<hex> *<имя>»
    public static Dictionary<string, string> ParseSums(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success) result[m.Groups[2].Value.Trim()] = m.Groups[1].Value.ToLowerInvariant();
        }
        return result;
    }

    // Скачать во временный файл, сверить SHA-256 и только потом положить на место
    public async Task DownloadAsync(UpdateAsset asset, string destination, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        if (asset.Sha256 is null)
            throw new UpdateException($"у файла {asset.Name} нет контрольной суммы — скачивание небезопасно");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";
        try
        {
            await DownloadToAsync(asset, part, progress, ct);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
        File.Move(part, destination, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task DownloadToAsync(UpdateAsset asset, string part, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        using (var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            using var sha = SHA256.Create();
            var buffer = new byte[1 << 16];
            long done = 0;
            var lastReport = DateTime.MinValue;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                sha.TransformBlock(buffer, 0, read, null, 0);
                done += read;
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                {
                    lastReport = DateTime.UtcNow;
                    progress.Report((done, total));
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            progress.Report((done, total));

            var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (actual != asset.Sha256)
                throw new UpdateException($"контрольная сумма {asset.Name} не совпала — файл повреждён или подменён");
        }
    }

    // Полная причина ошибки: сообщения вложенных исключений («see inner exception» само по себе ничего не говорит)
    public static string Describe(Exception ex)
    {
        if (ex is UpdateException) return ex.Message;
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message.Replace(" See inner exception.", "").Replace(", see inner exception.", ".").Trim();
            if (m.Length > 0 && !parts.Contains(m)) parts.Add(m);
        }
        return string.Join(" → ", parts);
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed class UpdateException(string message) : Exception(message);

// Версия вида 1.2.0 или 1.2.0-dev.3 (правила SemVer: 1.2.0-dev.3 < 1.2.0)
public sealed class SemVersion : IComparable<SemVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _pre;

    private SemVersion(int[] numbers, string[] pre, string text)
    {
        _numbers = numbers;
        _pre = pre;
        Text = text;
    }

    public string Text { get; }
    public bool IsPrerelease => _pre.Length > 0;
    public override string ToString() => Text;

    public static bool TryParse(string? value, out SemVersion version)
    {
        version = null!;
        var m = Regex.Match(value?.Trim() ?? "", @"^[vV]?(\d+(?:\.\d+){0,3})(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$");
        if (!m.Success) return false;
        var numbers = m.Groups[1].Value.Split('.').Select(int.Parse).ToList();
        while (numbers.Count < 3) numbers.Add(0);
        var pre = m.Groups[2].Success ? m.Groups[2].Value.Split('.') : Array.Empty<string>();
        version = new SemVersion(numbers.ToArray(), pre, m.Groups[1].Value + (pre.Length > 0 ? "-" + m.Groups[2].Value : ""));
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < Math.Max(_numbers.Length, other._numbers.Length); i++)
        {
            var a = i < _numbers.Length ? _numbers[i] : 0;
            var b = i < other._numbers.Length ? other._numbers[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        if (_pre.Length == 0 || other._pre.Length == 0)
            return other._pre.Length.CompareTo(_pre.Length);
        for (var i = 0; i < Math.Min(_pre.Length, other._pre.Length); i++)
        {
            var x = _pre[i];
            var y = other._pre[i];
            var xn = int.TryParse(x, out var xi);
            var yn = int.TryParse(y, out var yi);
            var c = xn && yn ? xi.CompareTo(yi) : xn ? -1 : yn ? 1 : string.CompareOrdinal(x, y);
            if (c != 0) return c;
        }
        return _pre.Length.CompareTo(other._pre.Length);
    }
}
