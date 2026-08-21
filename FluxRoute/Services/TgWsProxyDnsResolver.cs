using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace FluxRoute.Services;

/// <summary>Resolves Telegram WebSocket front domains without relying solely on ISP DNS.</summary>
internal sealed class TgWsProxyDnsResolver : IDisposable
{
    private static readonly string[] DohEndpoints =
    [
        "https://1.1.1.1/dns-query",
        "https://8.8.8.8/resolve"
    ];

    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(4)
    })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private readonly ConcurrentDictionary<string, (string Ip, long Expires)> _cache = new();
    private long _dohDownUntil;

    internal async Task<string?> ResolveIpv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out IPAddress? address))
            return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? host : null;

        if (_cache.TryGetValue(host, out var cached) && cached.Expires > Environment.TickCount64)
            return cached.Ip;

        bool anyDohResponse = false;
        if (Environment.TickCount64 >= Volatile.Read(ref _dohDownUntil))
        {
            foreach (string endpoint in DohEndpoints)
            {
                try
                {
                    var result = await QueryDohAsync(endpoint, host, cancellationToken).ConfigureAwait(false);
                    anyDohResponse |= result.Reachable;
                    if (!string.IsNullOrWhiteSpace(result.Ip))
                    {
                        _cache[host] = (result.Ip, Environment.TickCount64 + 300_000);
                        return result.Ip;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Try the next resolver and then the system resolver.
                }
            }

            if (!anyDohResponse)
                Volatile.Write(ref _dohDownUntil, Environment.TickCount64 + 30_000);
        }

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            IPAddress? ipv4 = addresses.FirstOrDefault(a =>
                a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 is not null)
            {
                _cache[host] = (ipv4.ToString(), Environment.TickCount64 + 300_000);
                return ipv4.ToString();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The caller will rotate to the next route.
        }

        return null;
    }

    private async Task<(string? Ip, bool Reachable)> QueryDohAsync(
        string endpoint, string host, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{endpoint}?name={Uri.EscapeDataString(host)}&type=A");
        request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");

        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (null, true);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("Answer", out JsonElement answer)
            || answer.ValueKind != JsonValueKind.Array)
            return (null, true);

        foreach (JsonElement item in answer.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.Number
                || type.GetInt32() != 1
                || !item.TryGetProperty("data", out JsonElement data))
                continue;

            string? ip = data.GetString();
            if (IPAddress.TryParse(ip, out IPAddress? address)
                && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return (address.ToString(), true);
        }

        return (null, true);
    }

    public void Dispose() => _http.Dispose();
}