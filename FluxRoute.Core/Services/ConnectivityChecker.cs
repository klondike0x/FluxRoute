using System.Net.Http;
using System.Net.NetworkInformation;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class CheckResult
{
    public string Key { get; init; } = "";
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
}

public static class ConnectivityChecker
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders =
    {
        { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
    }
    };

    public static readonly Dictionary<string, List<TargetEntry>> BuiltinSites = new()
    {
        ["YouTube"] =
        [
            new TargetEntry { Key = "YouTube", Kind = TargetKind.Http, Value = "https://www.youtube.com" },
            new TargetEntry { Key = "YouTubeImg", Kind = TargetKind.Http, Value = "https://i.ytimg.com" }
        ],
        ["Discord"] =
        [
            new TargetEntry { Key = "Discord", Kind = TargetKind.Http, Value = "https://discord.com" },
            new TargetEntry { Key = "DiscordGW", Kind = TargetKind.Http, Value = "https://gateway.discord.gg" }
        ],
        ["Google"] =
        [
            new TargetEntry { Key = "Google", Kind = TargetKind.Http, Value = "https://www.google.com" },
            new TargetEntry { Key = "GoogleDNS", Kind = TargetKind.Ping, Value = "8.8.8.8" }
        ],
        ["Twitch"] =
        [
            new TargetEntry { Key = "Twitch", Kind = TargetKind.Http, Value = "https://www.twitch.tv" }
        ],
        ["Instagram"] =
        [
            new TargetEntry { Key = "Instagram", Kind = TargetKind.Http, Value = "https://www.instagram.com" }
        ],
        ["Telegram"] =
        [
            new TargetEntry { Key = "Telegram", Kind = TargetKind.Http, Value = "https://web.telegram.org" },
            new TargetEntry { Key = "TelegramMe", Kind = TargetKind.Http, Value = "https://t.me" }
        ]
    };

    public static async Task<CheckResult> CheckAsync(TargetEntry target, CancellationToken ct = default)
    {
        return target.Kind == TargetKind.Ping
            ? await PingAsync(target, ct)
            : await HttpAsync(target, ct);
    }

    private static async Task<CheckResult> HttpAsync(TargetEntry target, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var resp = await _http.GetAsync(target.Value, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var ok = (int)resp.StatusCode < 500;
            return new CheckResult { Key = target.Key, Ok = ok, Detail = $"HTTP {(int)resp.StatusCode}" };
        }
        catch (OperationCanceledException)
        {
            return new CheckResult { Key = target.Key, Ok = false, Detail = "Таймаут" };
        }
        catch (Exception ex)
        {
            return new CheckResult { Key = target.Key, Ok = false, Detail = ex.Message.Split('\n')[0] };
        }
    }

    private static async Task<CheckResult> PingAsync(TargetEntry target, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Value, 3000);
            var ok = reply.Status == IPStatus.Success;
            return new CheckResult { Key = target.Key, Ok = ok, Detail = ok ? $"{reply.RoundtripTime} мс" : reply.Status.ToString() };
        }
        catch (Exception ex)
        {
            return new CheckResult { Key = target.Key, Ok = false, Detail = ex.Message.Split('\n')[0] };
        }
    }

    public static async Task<(double successRate, List<CheckResult> results)> CheckAllAsync(
        IEnumerable<TargetEntry> targets, CancellationToken ct = default)
    {
        var tasks = targets.Select(t => CheckAsync(t, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        var rate = results.Length == 0 ? 0.0 : results.Count(r => r.Ok) / (double)results.Length;
        return (rate, results.ToList());
    }
}
