using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.NetworkInformation;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

/// <summary>Имя именованного HttpClient для проверки связности.</summary>
public static class HttpClientNames
{
    public const string Connectivity = "connectivity";
}

public interface IConnectivityChecker
{
    bool IsCurlAvailable { get; }

    Task<CheckResult> CheckAsync(TargetEntry target, CancellationToken ct = default);
    Task<CheckResult> CheckAsync(TargetEntry target, bool useCurlForHttp, CancellationToken ct = default);

    Task<(double successRate, List<CheckResult> results)> CheckAllAsync(
        IEnumerable<TargetEntry> targets,
        CancellationToken ct = default);

    Task<(double successRate, List<CheckResult> results)> CheckAllAsync(
        IEnumerable<TargetEntry> targets,
        bool useCurlForHttp,
        int maxParallelChecks,
        CancellationToken ct = default);
}

public sealed class ConnectivityChecker : IConnectivityChecker
{
    private const int DefaultTimeoutSeconds = 8;
    private const int DefaultConnectTimeoutSeconds = 4;
    private const int DefaultMaxParallelChecks = 6;

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly Lazy<bool> _curlAvailable = new(CheckCurlAvailable);

    private readonly IHttpClientFactory _httpClientFactory;

    public ConnectivityChecker(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Fallback-конструктор для WPF designer и юнит-тестов.</summary>
    public ConnectivityChecker() : this(DefaultHttpClientFactory.Instance) { }

    public static readonly Dictionary<string, List<TargetEntry>> BuiltinSites = new()
    {
        ["YouTube"] =
        [
            new TargetEntry { Key = "YouTube", Kind = TargetKind.Http, Value = "https://www.youtube.com" },
            new TargetEntry { Key = "YouTubeImg", Kind = TargetKind.Http, Value = "https://i.ytimg.com" },
            new TargetEntry { Key = "YouTubeNoCookie", Kind = TargetKind.Http, Value = "https://www.youtube-nocookie.com" }
        ],
        ["Discord"] =
        [
            new TargetEntry { Key = "Discord", Kind = TargetKind.Http, Value = "https://discord.com" },
            new TargetEntry { Key = "DiscordGW", Kind = TargetKind.Http, Value = "https://gateway.discord.gg" },
            new TargetEntry { Key = "DiscordCDN", Kind = TargetKind.Http, Value = "https://cdn.discordapp.com" }
        ],
        ["Google"] =
        [
            new TargetEntry { Key = "Google", Kind = TargetKind.Http, Value = "https://www.google.com" },
            new TargetEntry { Key = "GoogleDNS", Kind = TargetKind.Ping, Value = "8.8.8.8" }
        ],
        ["Twitch"] =
        [
            new TargetEntry { Key = "Twitch", Kind = TargetKind.Http, Value = "https://www.twitch.tv" },
            new TargetEntry { Key = "TwitchStatic", Kind = TargetKind.Http, Value = "https://static-cdn.jtvnw.net" }
        ],
        ["Instagram"] =
        [
            new TargetEntry { Key = "Instagram", Kind = TargetKind.Http, Value = "https://www.instagram.com" },
            new TargetEntry { Key = "InstagramCDN", Kind = TargetKind.Http, Value = "https://scontent.cdninstagram.com" }
        ],
        ["Telegram"] =
        [
            new TargetEntry { Key = "TelegramWeb", Kind = TargetKind.Http, Value = "https://web.telegram.org" },
            new TargetEntry { Key = "TelegramMe", Kind = TargetKind.Http, Value = "https://t.me" }
        ]
    };

    public bool IsCurlAvailable => _curlAvailable.Value;

    public Task<CheckResult> CheckAsync(TargetEntry target, CancellationToken ct = default)
    {
        return CheckAsync(target, useCurlForHttp: true, ct);
    }

    public async Task<CheckResult> CheckAsync(
        TargetEntry target,
        bool useCurlForHttp,
        CancellationToken ct = default)
    {
        if (target.Kind == TargetKind.Ping)
            return await PingAsync(target, ct).ConfigureAwait(false);

        if (useCurlForHttp && IsCurlAvailable)
            return await CurlHttpAsync(target, ct).ConfigureAwait(false);

        return await HttpClientAsync(target, ct).ConfigureAwait(false);
    }

    public Task<(double successRate, List<CheckResult> results)> CheckAllAsync(
        IEnumerable<TargetEntry> targets,
        CancellationToken ct = default)
    {
        return CheckAllAsync(targets, useCurlForHttp: true, DefaultMaxParallelChecks, ct);
    }

    public async Task<(double successRate, List<CheckResult> results)> CheckAllAsync(
        IEnumerable<TargetEntry> targets,
        bool useCurlForHttp,
        int maxParallelChecks,
        CancellationToken ct = default)
    {
        var targetList = targets
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => $"{x.Kind}|{x.Key}|{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        if (targetList.Count == 0)
            return (0.0, new List<CheckResult>());

        var parallelism = Math.Clamp(maxParallelChecks, 1, 16);
        using var throttler = new SemaphoreSlim(parallelism, parallelism);

        var tasks = targetList.Select(async target =>
        {
            await throttler.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await CheckAsync(target, useCurlForHttp, ct).ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var rate = results.Count(x => x.Ok) / (double)results.Length;

        return (rate, results.ToList());
    }

    private async Task<CheckResult> CurlHttpAsync(TargetEntry target, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var value = NormalizeUrl(target.Value);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds + 2));

        try
        {
            using var process = StartCurl(value);
            if (process is null)
                return await HttpClientAsync(target, ct).ConfigureAwait(false);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            sw.Stop();

            var metrics = ParseCurlMetrics(stdout);
            var status = metrics.StatusCode;
            var elapsedMs = metrics.TotalSeconds is null
                ? sw.ElapsedMilliseconds
                : (long)Math.Round(metrics.TotalSeconds.Value * 1000);

            var ok = process.ExitCode == 0 && status is >= 200 and < 500;
            var detail = BuildCurlDetail(status, process.ExitCode, metrics.ErrorMessage, stderr);

            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = ok,
                StatusCode = status,
                ExitCode = process.ExitCode,
                ElapsedMs = elapsedMs,
                Detail = detail,
                Checker = "curl.exe"
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = false,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = "curl timeout",
                Checker = "curl.exe"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = false,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = $"curl: {FirstLine(ex.Message)}",
                Checker = "curl.exe"
            };
        }
    }

    private static Process? StartCurl(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "curl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var outputTarget = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        psi.ArgumentList.Add("--location");
        psi.ArgumentList.Add("--insecure");
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--show-error");
        psi.ArgumentList.Add("--connect-timeout");
        psi.ArgumentList.Add(DefaultConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--max-time");
        psi.ArgumentList.Add(DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputTarget);
        psi.ArgumentList.Add("--user-agent");
        psi.ArgumentList.Add(BrowserUserAgent);
        psi.ArgumentList.Add("--write-out");
        psi.ArgumentList.Add("FR_CURL_RESULT:%{http_code}|%{time_total}|%{remote_ip}|%{errormsg}");
        psi.ArgumentList.Add(url);

        return Process.Start(psi);
    }

    private async Task<CheckResult> HttpClientAsync(TargetEntry target, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var value = NormalizeUrl(target.Value);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

            using var http = _httpClientFactory.CreateClient(HttpClientNames.Connectivity);
            using var request = new HttpRequestMessage(HttpMethod.Get, value);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token).ConfigureAwait(false);

            sw.Stop();

            var status = (int)response.StatusCode;
            var ok = status < 500;

            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = ok,
                StatusCode = status,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = $"HTTP {status}",
                Checker = "HttpClient"
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = false,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = "Таймаут",
                Checker = "HttpClient"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = value,
                Ok = false,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = FirstLine(ex.Message),
                Checker = "HttpClient"
            };
        }
    }

    private static async Task<CheckResult> PingAsync(TargetEntry target, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Value, 3000).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            sw.Stop();

            var ok = reply.Status == IPStatus.Success;

            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = target.Value,
                Ok = ok,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = ok ? $"{reply.RoundtripTime} мс" : reply.Status.ToString(),
                Checker = "Ping"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CheckResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Value = target.Value,
                Ok = false,
                ElapsedMs = sw.ElapsedMilliseconds,
                Detail = FirstLine(ex.Message),
                Checker = "Ping"
            };
        }
    }

    private static string NormalizeUrl(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"https://{trimmed}";
    }

    private static bool CheckCurlAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("--version");

            if (!process.Start())
                return false;

            if (!process.WaitForExit(1500))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static CurlMetrics ParseCurlMetrics(string stdout)
    {
        var markerLine = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(x => x.StartsWith("FR_CURL_RESULT:", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(markerLine))
            return new CurlMetrics(null, null, null);

        var payload = markerLine["FR_CURL_RESULT:".Length..];
        var parts = payload.Split('|', 4);

        int? status = null;
        if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStatus))
            status = parsedStatus;

        double? totalSeconds = null;
        if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds))
            totalSeconds = parsedSeconds;

        var error = parts.Length > 3 ? parts[3].Trim() : null;

        return new CurlMetrics(status, totalSeconds, string.IsNullOrWhiteSpace(error) ? null : error);
    }

    private static string BuildCurlDetail(int? status, int exitCode, string? curlError, string stderr)
    {
        var cleanError = !string.IsNullOrWhiteSpace(curlError)
            ? curlError
            : FirstLine(stderr);

        if (exitCode == 0 && status is not null)
            return $"HTTP {status} через curl.exe";

        if (status is not null && status > 0)
            return string.IsNullOrWhiteSpace(cleanError)
                ? $"curl exit {exitCode}, HTTP {status}"
                : $"curl exit {exitCode}, HTTP {status}: {cleanError}";

        return string.IsNullOrWhiteSpace(cleanError)
            ? $"curl exit {exitCode}"
            : $"curl exit {exitCode}: {cleanError}";
    }

    private static string FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Не критично: процесс мог завершиться сам.
        }
    }

    private sealed record CurlMetrics(int? StatusCode, double? TotalSeconds, string? ErrorMessage);
}
