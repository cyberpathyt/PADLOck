using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PADLOck.Services;

public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(SemVersion Version, string Tag, bool Prerelease, string Notes, string PageUrl,
                                 UpdateAsset? Installer, UpdateAsset? Apk);

// Обновления из GitHub Releases.
// Релиз с галочкой «Pre-release» — канал dev, обычный — stable.
// В релизе: установщик PADLOck-Setup-<версия>.exe и (по желанию) APK FreeKiosk — ровно тот, которым прошивать.
// Контрольная сумма берётся из поля digest, которое GitHub считает сам, или из файла SHA256SUMS.txt в релизе.
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    // Адрес API можно подменить (для проверки на тестовом сервере)
    public static string ApiBase { get; set; } = "https://api.github.com";

    private static HttpClient CreateClient()
    {
        // Корпоративный прокси с авторизацией Windows
        HttpClient.DefaultProxy.Credentials = CredentialCache.DefaultCredentials;
        var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = false }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version.Replace('+', '.')));
        return client;
    }

    // Самый новый релиз канала (dev включает и stable). null — релизов нет
    public async Task<ReleaseInfo?> FindLatestAsync(bool dev, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/repos/{AppInfo.UpdateRepository}/releases?per_page=30");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        using var response = await Http.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new UpdateException($"репозиторий {AppInfo.UpdateRepository} не найден или закрыт");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new UpdateException("GitHub временно ограничил запросы с этого адреса — проверка повторится при следующем запуске");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        ReleaseInfo? best = null;
        string? bestSums = null;
        var bestHasManyApks = false;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("draft").GetBoolean()) continue;
            var prerelease = r.GetProperty("prerelease").GetBoolean();
            if (prerelease && !dev) continue;

            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!SemVersion.TryParse(tag, out var version)) continue;

            UpdateAsset? installer = null, apk = null;
            string? sums = null;
            var apkCount = 0;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                var size = a.GetProperty("size").GetInt64();
                var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                var asset = new UpdateAsset(name, url, size, sha);

                if (name.StartsWith("PADLOck-Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    installer = asset;
                else if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    apk = asset;
                    apkCount++;
                }
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    sums = url;
            }

            var info = new ReleaseInfo(version, tag, prerelease, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                                       r.GetProperty("html_url").GetString() ?? "", installer, apk);
            if (best is null || version.CompareTo(best.Version) > 0)
            {
                best = info;
                bestSums = sums;
                bestHasManyApks = apkCount > 1;
            }
        }

        // Непонятно, каким APK прошивать — такой релиз не берём
        if (bestHasManyApks)
            throw new UpdateException($"в релизе {best!.Tag} больше одного APK — оставьте один");

        // Суммы из SHA256SUMS.txt — для файлов, у которых GitHub не посчитал digest
        if (best is not null && bestSums is { } sumsUrl
            && (best.Installer is { Sha256: null } || best.Apk is { Sha256: null }))
        {
            var sums = ParseSums(await Http.GetStringAsync(sumsUrl, timeout.Token));
            best = best with
            {
                Installer = WithSum(best.Installer, sums),
                Apk = WithSum(best.Apk, sums)
            };
        }
        return best;
    }

    private static UpdateAsset? WithSum(UpdateAsset? a, Dictionary<string, string> sums) =>
        a is { Sha256: null } && sums.TryGetValue(a.Name, out var s) ? a with { Sha256 = s } : a;

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
            throw new UpdateException($"у файла {asset.Name} нет контрольной суммы (digest или SHA256SUMS.txt) — скачивание небезопасно");

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
