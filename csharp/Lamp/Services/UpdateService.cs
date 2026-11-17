using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lamp.Services;

public sealed record UpdateOffer(string Version, string Current, long AssetId, string Sha256);

public static class UpdateService
{
    const string Owner = "J0shGG";
    const string Repo = "seychas";
    const string SetupName = "Seychas-Setup.exe";
    const string LatestUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    static readonly Regex VersionRe = new(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);
    static readonly Regex ShaRe = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    static readonly HttpClient Http = CreateHttp();
    static readonly object CacheGate = new();

    static string? _cacheLocal;
    static UpdateOffer? _cacheOffer;
    static bool _cached;
    static Task<string>? _downloadJob;

    static HttpClient CreateHttp()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Seychas");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    public static Task<UpdateOffer?> CheckAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(SetupService.Version, cancellationToken);

    public static async Task<UpdateOffer?> CheckAsync(string localVersion, CancellationToken cancellationToken = default)
    {
        lock (CacheGate)
        {
            if (_cached && _cacheLocal == localVersion) return _cacheOffer;
        }
        try
        {
            var offer = await FindUpdate(localVersion, cancellationToken).ConfigureAwait(false);
            lock (CacheGate)
            {
                _cacheLocal = localVersion;
                _cacheOffer = offer;
                _cached = true;
            }
            return offer;
        }
        catch
        {
            return null;
        }
    }

    public static Task<string> DownloadAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var running = _downloadJob;
        if (running is { IsCompleted: false }) return running;
        var job = RunDownload(progress, cancellationToken);
        _downloadJob = job;
        _ = job.ContinueWith(_ =>
        {
            if (ReferenceEquals(_downloadJob, job)) _downloadJob = null;
        }, TaskScheduler.Default);
        return job;
    }

    public static bool TryOpenInstaller(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task<UpdateOffer?> FindUpdate(string localVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var res = await Http.SendAsync(req, timeout.Token).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        return UpdateFromRelease(doc.RootElement, localVersion);
    }

    static UpdateOffer? UpdateFromRelease(JsonElement release, string localVersion)
    {
        var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        var version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) && tag.Length > 0
            ? tag[1..]
            : tag;
        if (!IsNewer(version, localVersion)) return null;
        var localParts = ParseVersion(localVersion);
        if (localParts is null) return null;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement asset = default;
        var found = false;
        foreach (var item in assets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("name", out var nameEl) || nameEl.GetString() != SetupName) continue;
            asset = item;
            found = true;
            break;
        }
        if (!found) return null;
        if (!asset.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var assetId) || assetId <= 0)
            return null;
        var digest = asset.TryGetProperty("digest", out var digestEl) ? digestEl.GetString() ?? "" : "";
        const string prefix = "sha256:";
        var sha256 = digest.StartsWith(prefix, StringComparison.Ordinal) ? digest[prefix.Length..].ToLowerInvariant() : "";
        if (!ShaRe.IsMatch(sha256)) return null;
        return new UpdateOffer(version, string.Join('.', localParts), assetId, sha256);
    }

    static bool IsNewer(string remote, string local)
    {
        var next = ParseVersion(remote);
        var current = ParseVersion(local);
        if (next is null || current is null) return false;
        for (var i = 0; i < 3; i++)
        {
            if (next[i] != current[i]) return next[i] > current[i];
        }
        return false;
    }

    static int[]? ParseVersion(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) && raw.Length > 0)
            raw = raw[1..];
        var match = VersionRe.Match(raw);
        if (!match.Success) return null;
        return
        [
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value),
        ];
    }

    static async Task<string> RunDownload(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var update = await CheckAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("no update");
        var dest = Path.Combine(Path.GetTempPath(), $"Seychas-Setup-{update.Version}.exe");
        var part = dest + ".part";
        try { if (File.Exists(part)) File.Delete(part); } catch { /* leftover lock */ }

        using var res = await SendAssetAsync(update.AssetId, cancellationToken).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        await using var body = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        byte[] digest;
        try
        {
            digest = await StreamToFile(body, part, total, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }

        byte[] expected;
        try { expected = Convert.FromHexString(update.Sha256); }
        catch
        {
            try { File.Delete(part); } catch { }
            throw new InvalidOperationException("digest");
        }
        if (!CryptographicOperations.FixedTimeEquals(digest, expected))
        {
            try { File.Delete(part); } catch { }
            throw new InvalidOperationException("digest");
        }

        try { if (File.Exists(dest)) File.Delete(dest); } catch { }
        File.Move(part, dest, true);
        return dest;
    }

    static async Task<HttpResponseMessage> SendAssetAsync(long assetId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        using var req = new HttpRequestMessage(HttpMethod.Get, AssetUrl(assetId));
        req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        return await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
    }

    static string AssetUrl(long assetId) =>
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/assets/{assetId}";

    static async Task<byte[]> StreamToFile(
        Stream source,
        string dest,
        long total,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var file = new FileStream(
            dest,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long received = 0;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);
        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(), stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("timeout");
            }
            if (read == 0) break;
            stall.CancelAfter(StallTimeout);
            hasher.AppendData(buffer.AsSpan(0, read));
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(total > 0 ? Math.Min(1, (double)received / total) : -1);
        }
        return hasher.GetHashAndReset();
    }
}
