using System.Buffers;
using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace FluxRoute.Services;

internal sealed class TgWsProxyOptions
{
    internal string ListenHost { get; init; } = "127.0.0.1";
    internal int ListenPort { get; init; } = 1443;
    internal byte[] Secret { get; init; } = Array.Empty<byte>();
    internal IReadOnlyDictionary<int, string> DataCenterTargets { get; init; } =
        new Dictionary<int, string>();
    internal bool CloudflareEnabled { get; init; } = true;
    internal bool CloudflarePriority { get; init; } = true;
    internal bool CloudflareDomainEnabled { get; init; }
    internal string CloudflareDomain { get; init; } = string.Empty;
    internal IReadOnlyList<string> CloudflareWorkerDomains { get; init; } = Array.Empty<string>();
    internal int BufferSize { get; init; } = 256 * 1024;
    internal int MaxConcurrentSessions { get; init; } = 4;
    internal bool PreferIPv4 { get; init; } = true;
    internal bool Verbose { get; init; }
}

/// <summary>Embedded local MTProto-to-WebSocket bridge.</summary>
internal sealed class TgWsProxyServer : IDisposable
{
    private static readonly string[] DefaultDataCenterHosts =
    [
        "149.154.175.50",
        "149.154.167.51",
        "149.154.175.100",
        "149.154.167.91",
        "149.154.171.5"
    ];
    private static readonly IReadOnlyDictionary<int, string> DefaultSpecialDataCenterHosts =
        new Dictionary<int, string> { [203] = "91.105.192.100" };

    private static readonly string[] DefaultTestDataCenterHosts =
    [
        "149.154.175.10",
        "149.154.167.40",
        "149.154.175.117"
    ];

    // Keep the upstream fallback pool available even when no custom Cloudflare
    // domain or Worker has been configured.
    private static readonly string[] DefaultCloudflareDomains =
    [
        "pclead.co.uk", "offshor.co.uk", "cakeisalie.co.uk", "noskomnadzor.co.uk",
        "lovetrue.co.uk", "sorokdva.co.uk", "pyatdesyatdva.co.uk", "kartoshka.co.uk",
        "sorokodin.co.uk", "pyatdesyatodin.co.uk", "notelega.co.uk", "ebally.co.uk",
        "nebally.co.uk", "havegreatday.co.uk", "pomogite.co.uk", "fixtelega.co.uk",
        "sadnews.co.uk", "onedaychamp.co.uk", "stopblocking.co.uk", "nothingthere.co.uk"
    ];

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, Task> _sessions = new();
    private CancellationTokenSource? _stopCts;
    private TcpListener? _listener;
    private TgWsProxyOptions? _options;
    private int _sessionId;
    private readonly TgWsProxyDnsResolver _dnsResolver = new();
    private readonly ConcurrentDictionary<string, long> _routeCooldowns = new();
    private readonly ConcurrentDictionary<int, string> _preferredRoutes = new();

    private readonly record struct WebSocketCandidate(
        string Target, string Host, string RouteKey, bool ResolveTarget, bool IsFront);

    private sealed class UpstreamDidNotRelayException(string message) : IOException(message);

    internal bool IsRunning { get; private set; }
    internal event Action<string>? Log;

    internal void Start(TgWsProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Secret.Length != 16)
            throw new ArgumentException("TG Proxy secret must contain 16 bytes.", nameof(options));
        if (options.ListenPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options.ListenPort));

        lock (_gate)
        {
            if (IsRunning) return;
            IPAddress address = ResolveListenAddress(options.ListenHost);
            _listener = new TcpListener(address, options.ListenPort);
            _listener.Start();
            _stopCts = new CancellationTokenSource();
            _options = options;
            IsRunning = true;
        }

        WriteLog($"▶ TG WS Proxy (C#) слушает {options.ListenHost}:{options.ListenPort}");
        var sessionPool = new SemaphoreSlim(
            Math.Clamp(options.MaxConcurrentSessions, 1, 64),
            Math.Clamp(options.MaxConcurrentSessions, 1, 64));
        _ = AcceptLoopAsync(_listener, _stopCts.Token, sessionPool);
    }

    internal void Stop()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            cts = _stopCts;
            listener = _listener;
            _stopCts = null;
            _listener = null;
            _options = null;
        }

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        WriteLog("⏹ TG WS Proxy остановлен");
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct, SemaphoreSlim sessionPool)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                var listenerOptions = _options;
                client.ReceiveBufferSize = listenerOptions?.BufferSize ?? 256 * 1024;
                client.SendBufferSize = listenerOptions?.BufferSize ?? 256 * 1024;
                if (!sessionPool.Wait(0))
                {
                    client.Dispose();
                    if (listenerOptions?.Verbose == true)
                        WriteLog("⚠ Пул TG Proxy заполнен: новое подключение отклонено");
                    continue;
                }

                int id = Interlocked.Increment(ref _sessionId);
                var task = HandleClientAsync(id, client, ct);
                _sessions[id] = task;
                _ = task.ContinueWith(_ =>
                {
                    _sessions.TryRemove(id, out Task? removedTask);
                    try { sessionPool.Release(); } catch (ObjectDisposedException) { }
                },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
        catch (SocketException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            WriteLog($"⚠ Ошибка listener TG Proxy: {ex.Message}");
        }
    }

    private async Task HandleClientAsync(int id, TcpClient client, CancellationToken serverToken)
    {
        using (client)
        using (NetworkStream local = client.GetStream())
        {
            var options = _options;
            if (options is null) return;

            try
            {
                byte[] handshake = await ReadExactAsync(local, TgWsProxyProtocol.HandshakeSize,
                    TimeSpan.FromSeconds(10), serverToken);
                if (!TgWsProxyProtocol.TryDecodeHandshake(handshake, options.Secret, out var info) || info is null)
                {
                    WriteLog($"⚠ #{id}: неверный MTProto handshake");
                    return;
                }

                byte[] relayHandshake = TgWsProxyProtocol.CreateRelayHandshake(info.ProtocolTag, info.WireDataCenter);
                using var crypto = TgWsProxyProtocol.CreateCrypto(info.ClientKeyMaterial, options.Secret, relayHandshake);
                WriteLog($"#{id}: DC{info.DataCenter}{(info.IsMedia ? " media" : string.Empty)} подключение");

                foreach (var candidate in OrderWebSocketCandidates(info, options))
                {
                    string? candidateTarget = candidate.ResolveTarget
                        ? await _dnsResolver.ResolveIpv4Async(candidate.Target, serverToken)
                        : candidate.Target;
                    if (string.IsNullOrWhiteSpace(candidateTarget))
                    {
                        MarkRouteFailure(info.DataCenter, candidate.RouteKey, candidate.IsFront);
                        if (options.Verbose)
                            WriteLog($"#{id}: DNS не разрешил {candidate.Host}");
                        continue;
                    }

                    try
                    {
                        await using var ws = await TgWsSocket.ConnectAsync(
                            candidateTarget, candidate.Host, candidate.Host,
                            info.IsTest ? "/apiws_test" : "/apiws",
                            options.BufferSize, options.PreferIPv4, TimeSpan.FromSeconds(8), serverToken);
                        await ws.SendBinaryAsync(relayHandshake, serverToken);
                        WriteLog($"#{id}: WebSocket {candidate.Host} подключён через {candidateTarget}");
                        await BridgeWebSocketAsync(local, ws, crypto, relayHandshake,
                            info.TransportWord, options, serverToken);
                        MarkRouteSuccess(info.DataCenter, candidate.RouteKey);
                        return;
                    }
                    catch (OperationCanceledException) when (serverToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        MarkRouteFailure(info.DataCenter, candidate.RouteKey, candidate.IsFront);
                        if (options.Verbose || ex is UpstreamDidNotRelayException)
                            WriteLog($"#{id}: WS {candidate.Host} недоступен — {ex.Message}");
                    }
                }

                string target = ResolveDataCenterTarget(info.DataCenter, info.IsTest, options);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    try
                    {
                        await BridgeTcpAsync(local, target, relayHandshake, crypto, options, serverToken);
                        WriteLog($"#{id}: TCP fallback {target}:443 завершён");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        WriteLog($"#{id}: TCP fallback не удался — {ex.Message}");
                    }
                }
                else WriteLog($"#{id}: для DC{info.DataCenter} нет upstream target");
            }
            catch (OperationCanceledException) when (serverToken.IsCancellationRequested) { }
            catch (EndOfStreamException) { }
            catch (Exception ex)
            {
                WriteLog($"⚠ #{id}: ошибка сессии — {ex.Message}");
            }
        }
    }

    private static async Task BridgeWebSocketAsync(NetworkStream local, TgWsSocket ws,
        TgWsProxyProtocol.CryptoBridge crypto, byte[] relayHandshake,
        uint transport, TgWsProxyOptions options, CancellationToken serverToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        var ct = linked.Token;
        var splitter = new TgPacketSplitter(relayHandshake, transport);
        long firstClientByteAt = 0;
        int upstreamResponded = 0;
        var noRelay = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ClientToUpstream()
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16 * 1024, options.BufferSize));
            try
            {
                while (true)
                {
                    int read = await local.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0)
                    {
                        foreach (var tail in splitter.Flush())
                            await ws.SendBinaryAsync(tail, ct);
                        break;
                    }

                    Interlocked.CompareExchange(ref firstClientByteAt, Environment.TickCount64, 0);
                    byte[] plain = crypto.ClientDecrypt.Transform(buffer.AsSpan(0, read));
                    byte[] encrypted = crypto.UpstreamEncrypt.Transform(plain);
                    foreach (var packet in splitter.Push(encrypted))
                        await ws.SendBinaryAsync(packet, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                linked.Cancel();
            }
        }

        async Task UpstreamToClient()
        {
            try
            {
                while (true)
                {
                    byte[]? packet = await ws.ReceiveDataAsync(ct);
                    if (packet is null) break;
                    Interlocked.Exchange(ref upstreamResponded, 1);
                    byte[] plain = crypto.UpstreamDecrypt.Transform(packet);
                    byte[] encrypted = crypto.ClientEncrypt.Transform(plain);
                    await local.WriteAsync(encrypted, ct);
                    await local.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { linked.Cancel(); }
        }

        async Task MonitorFirstResponse()
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(500, ct);
                    long firstByte = Volatile.Read(ref firstClientByteAt);
                    if (firstByte != 0 && Volatile.Read(ref upstreamResponded) == 0
                        && Environment.TickCount64 - firstByte >= 10_000)
                    {
                        noRelay.TrySetResult(true);
                        linked.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        Task clientTask = ClientToUpstream();
        Task upstreamTask = UpstreamToClient();
        _ = MonitorFirstResponse();
        Task first = await Task.WhenAny(clientTask, upstreamTask, noRelay.Task);
        if (first == noRelay.Task)
            throw new UpstreamDidNotRelayException("Telegram не ответил через этот WebSocket-маршрут за 10 секунд");

        linked.Cancel();
        if (first == upstreamTask && Volatile.Read(ref upstreamResponded) == 0
            && Volatile.Read(ref firstClientByteAt) != 0)
        {
            if (serverToken.IsCancellationRequested) return;
            try { await upstreamTask; }
            catch (Exception ex) { throw new UpstreamDidNotRelayException(ex.Message); }
            throw new UpstreamDidNotRelayException("WebSocket закрылся до первого ответа Telegram");
        }

        try { await first; } catch { }
    }

    private static async Task BridgeTcpAsync(NetworkStream local, string target,
        byte[] relayHandshake, TgWsProxyProtocol.CryptoBridge crypto,
        TgWsProxyOptions options, CancellationToken serverToken)
    {
        using var upstream = options.PreferIPv4
            ? new TcpClient(AddressFamily.InterNetwork)
            : new TcpClient();
        upstream.NoDelay = true;
        upstream.ReceiveBufferSize = options.BufferSize;
        upstream.SendBufferSize = options.BufferSize;
        await upstream.ConnectAsync(target, 443, serverToken);
        using NetworkStream remote = upstream.GetStream();
        await remote.WriteAsync(relayHandshake.ToArray(), serverToken);
        await remote.FlushAsync(serverToken);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        var ct = linked.Token;

        async Task ForwardUp()
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16 * 1024, options.BufferSize));
            try
            {
                while (true)
                {
                    int read = await local.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;
                    byte[] plain = crypto.ClientDecrypt.Transform(buffer.AsSpan(0, read));
                    await remote.WriteAsync(crypto.UpstreamEncrypt.Transform(plain), ct);
                    await remote.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { ArrayPool<byte>.Shared.Return(buffer); linked.Cancel(); }
        }

        async Task ForwardDown()
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16 * 1024, options.BufferSize));
            try
            {
                while (true)
                {
                    int read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;
                    byte[] plain = crypto.UpstreamDecrypt.Transform(buffer.AsSpan(0, read));
                    await local.WriteAsync(crypto.ClientEncrypt.Transform(plain), ct);
                    await local.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { ArrayPool<byte>.Shared.Return(buffer); linked.Cancel(); }
        }

        Task first = await Task.WhenAny(ForwardUp(), ForwardDown());
        linked.Cancel();
        try { await first; } catch { }
    }

    private IReadOnlyList<WebSocketCandidate> OrderWebSocketCandidates(
        TgWsProxyProtocol.HandshakeInfo info, TgWsProxyOptions options)
    {
        int dc = TgWsProxyProtocol.GetWebSocketDataCenter(info.DataCenter);
        var candidates = BuildWebSocketCandidates(info, options).ToList();
        var fresh = candidates.Where(candidate => !IsRouteCooling(dc, candidate.RouteKey)).ToList();
        if (fresh.Count == 0) fresh = candidates;

        if (_preferredRoutes.TryGetValue(dc, out string? preferred))
        {
            var preferredCandidates = fresh.Where(candidate => candidate.RouteKey == preferred).ToList();
            fresh = preferredCandidates.Concat(fresh.Where(candidate => candidate.RouteKey != preferred)).ToList();
        }

        return fresh;
    }

    private IReadOnlyList<WebSocketCandidate> BuildWebSocketCandidates(
        TgWsProxyProtocol.HandshakeInfo info, TgWsProxyOptions options)
    {
        string directTarget = ResolveDataCenterTarget(info.DataCenter, info.IsTest, options);
        int domainDataCenter = TgWsProxyProtocol.GetWebSocketDataCenter(info.DataCenter);
        string normalA = $"kws{domainDataCenter}.web.telegram.org";
        string normalB = $"kws{domainDataCenter}-1.web.telegram.org";
        var hosts = info.IsMedia ? new[] { normalB, normalA } : new[] { normalA, normalB };
        var candidates = new List<WebSocketCandidate>();

        foreach (string host in hosts)
        {
            string target = string.IsNullOrWhiteSpace(directTarget) ? host : directTarget;
            candidates.Add(new WebSocketCandidate(target, host, $"direct:{domainDataCenter}", false, false));
        }

        var frontCandidates = new List<WebSocketCandidate>();
        if (options.CloudflareEnabled && options.CloudflareDomainEnabled
            && !string.IsNullOrWhiteSpace(options.CloudflareDomain))
        {
            string baseDomain = options.CloudflareDomain.Trim().TrimEnd('.');
            string cfHost = baseDomain.Contains("{dc}", StringComparison.OrdinalIgnoreCase)
                ? baseDomain.Replace("{dc}", domainDataCenter.ToString(), StringComparison.OrdinalIgnoreCase)
                : $"kws{domainDataCenter}.{baseDomain}";
            frontCandidates.Add(new WebSocketCandidate(cfHost, cfHost, $"front:{cfHost}", true, true));
        }

        if (options.CloudflareEnabled)
        {
            foreach (string workerDomain in options.CloudflareWorkerDomains)
            {
                string workerHost = workerDomain.Trim().TrimEnd('.');
                if (string.IsNullOrWhiteSpace(workerHost)) continue;
                frontCandidates.Add(new WebSocketCandidate(workerHost, workerHost,
                    $"front:{workerHost}", true, true));
            }

            foreach (string baseDomain in DefaultCloudflareDomains)
            {
                string frontHost = $"kws{domainDataCenter}.{baseDomain}";
                frontCandidates.Add(new WebSocketCandidate(frontHost, frontHost,
                    $"front:{frontHost}", true, true));
            }
        }

        if (options.CloudflarePriority)
            candidates.InsertRange(0, frontCandidates);
        else
            candidates.AddRange(frontCandidates);
        return candidates;
    }

    private bool IsRouteCooling(int dc, string routeKey)
    {
        string key = $"{dc}|{routeKey}";
        if (!_routeCooldowns.TryGetValue(key, out long expires)) return false;
        if (expires > Environment.TickCount64) return true;
        _routeCooldowns.TryRemove(key, out _);
        return false;
    }

    private void MarkRouteFailure(int dataCenter, string routeKey, bool isFront)
    {
        int dc = TgWsProxyProtocol.GetWebSocketDataCenter(dataCenter);
        _routeCooldowns[$"{dc}|{routeKey}"] = Environment.TickCount64
            + (isFront ? 30_000 : 60_000);
    }

    private void MarkRouteSuccess(int dataCenter, string routeKey)
    {
        int dc = TgWsProxyProtocol.GetWebSocketDataCenter(dataCenter);
        _routeCooldowns.TryRemove($"{dc}|{routeKey}", out _);
        _preferredRoutes[dc] = routeKey;
    }

    private static string ResolveDataCenterTarget(int dataCenter, bool isTest, TgWsProxyOptions options)
    {
        if (options.DataCenterTargets.TryGetValue(dataCenter, out string? configured)
            && !string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        if (!isTest && DefaultSpecialDataCenterHosts.TryGetValue(dataCenter, out string? special))
            return special;
        if (isTest)
            return dataCenter is >= 1 and <= 3 ? DefaultTestDataCenterHosts[dataCenter - 1] : string.Empty;
        return dataCenter is >= 1 and <= 5 ? DefaultDataCenterHosts[dataCenter - 1] : string.Empty;
    }

    private static IPAddress ResolveListenAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address)) return address;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;
        return IPAddress.Loopback;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeoutCts.Token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private void WriteLog(string text)
    {
        try { Log?.Invoke(text); } catch { }
    }

    public void Dispose() { Stop(); _dnsResolver.Dispose(); }
}