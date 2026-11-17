using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lamp.Services;

public sealed class PresenceService : IDisposable
{
    const string Topic = "seychas-online-j0shgg-v1k8p3";
    static readonly TimeSpan Window = TimeSpan.FromMinutes(14);
    static readonly TimeSpan Beat = TimeSpan.FromMinutes(6);
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);
    static readonly Regex IdRe = new("^[a-f0-9]{32}$", RegexOptions.Compiled);

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    readonly string _id = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
    readonly Action<int> _onCount;
    readonly CancellationTokenSource _cts = new();
    DateTimeOffset _selfAt;
    DateTimeOffset _pauseBeatsUntil;

    public PresenceService(Action<int> onCount)
    {
        _onCount = onCount;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Lamp");
        _ = RunAsync();
    }

    async Task RunAsync()
    {
        await BeatAsync();
        await PollAsync();
        _ = LoopAsync(Beat, BeatAsync);
        _ = LoopAsync(Poll, PollAsync);
    }

    async Task LoopAsync(TimeSpan delay, Func<Task> work)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, _cts.Token);
                await work();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                /* keep the desk running if ntfy is down */
            }
        }
    }

    async Task BeatAsync()
    {
        if (DateTimeOffset.UtcNow < _pauseBeatsUntil) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{Topic}")
            {
                Content = new StringContent(_id),
            };
            req.Headers.TryAddWithoutValidation("Priority", "min");
            using var res = await _http.SendAsync(req, _cts.Token);
            if ((int)res.StatusCode == 429)
            {
                _pauseBeatsUntil = DateTimeOffset.UtcNow.AddHours(6);
                return;
            }
            res.EnsureSuccessStatusCode();
            _selfAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            /* silent: the number simply hides */
        }
    }

    async Task PollAsync()
    {
        try
        {
            var since = DateTimeOffset.UtcNow.Subtract(Window).ToUnixTimeSeconds();
            var body = await _http.GetStringAsync($"https://ntfy.sh/{Topic}/json?poll=1&since={since}", _cts.Token);
            var count = CountOnline(body, DateTimeOffset.UtcNow, _id, _selfAt);
            if (count > 0) _onCount(count);
        }
        catch
        {
            /* hide the number */
        }
    }

    public static int CountOnline(string body, DateTimeOffset now, string selfId, DateTimeOffset selfAt)
    {
        var ids = new HashSet<string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.GetProperty("event").GetString() != "message") continue;
                var message = root.GetProperty("message").GetString() ?? "";
                if (!IdRe.IsMatch(message)) continue;
                var at = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("time").GetInt64());
                if (now - at > Window) continue;
                ids.Add(message);
            }
            catch
            {
                /* skip a broken line */
            }
        }
        if (now - selfAt <= Window) ids.Add(selfId);
        return ids.Count;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
    }
}
