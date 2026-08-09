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

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, Task> _sessions = new();
    private CancellationTokenSource? _stopCts;
    private TcpListener? _listener;
    private TgWsProxyOptions? _options;
    private int _sessionId;

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
        _ = AcceptLoopAsync(_listener, _stopCts.Token);
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

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
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
                int id = Interlocked.Increment(ref _sessionId);
                var task = HandleClientAsync(id, client, ct);
                _sessions[id] = task;
                _ = task.ContinueWith(_ =>
                {
                    _sessions.TryRemove(id, out Task? removedTask);
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

                foreach (var candidate in BuildWebSocketCandidates(info, options))
                {
                    try
                    {
                        await using var ws = await TgWsSocket.ConnectAsync(
                            candidate.Target, candidate.Host, candidate.Host,
                            info.IsTest ? "/apiws_test" : "/apiws",
                            options.BufferSize, TimeSpan.FromSeconds(8), serverToken);
                        await ws.SendBinaryAsync(relayHandshake, serverToken);
                        WriteLog($"#{id}: WebSocket {candidate.Host} подключён");
                        await BridgeWebSocketAsync(local, ws, crypto, relayHandshake,
                            info.TransportWord, options, serverToken);
                        return;
                    }
                    catch (OperationCanceledException) when (serverToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (options.Verbose)
                            WriteLog($"#{id}: WS {candidate.Host} недоступен — {ex.Message}");
                    }
                }

                string target = ResolveDataCenterTarget(info.DataCenter, options);
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
                    byte[] plain = crypto.UpstreamDecrypt.Transform(packet);
                    byte[] encrypted = crypto.ClientEncrypt.Transform(plain);
                    await local.WriteAsync(encrypted, ct);
                    await local.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { linked.Cancel(); }
        }

        Task first = await Task.WhenAny(ClientToUpstream(), UpstreamToClient());
        linked.Cancel();
        try { await first; } catch { }
    }

    private static async Task BridgeTcpAsync(NetworkStream local, string target,
        byte[] relayHandshake, TgWsProxyProtocol.CryptoBridge crypto,
        TgWsProxyOptions options, CancellationToken serverToken)
    {
        using var upstream = new TcpClient { NoDelay = true, ReceiveBufferSize = options.BufferSize, SendBufferSize = options.BufferSize };
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

    private static IReadOnlyList<(string Target, string Host)> BuildWebSocketCandidates(
        TgWsProxyProtocol.HandshakeInfo info, TgWsProxyOptions options)
    {
        string target = ResolveDataCenterTarget(info.DataCenter, options);
        string normalA = $"kws{info.DataCenter}.web.telegram.org";
        string normalB = $"kws{info.DataCenter}-1.web.telegram.org";
        var hosts = info.IsMedia ? new[] { normalB, normalA } : new[] { normalA, normalB };
        var candidates = new List<(string, string)>();

        foreach (string host in hosts)
            candidates.Add((string.IsNullOrWhiteSpace(target) ? host : target, host));

        if (options.CloudflareEnabled && options.CloudflareDomainEnabled
            && !string.IsNullOrWhiteSpace(options.CloudflareDomain))
        {
            string baseDomain = options.CloudflareDomain.Trim().TrimEnd('.');
            string cfHost = baseDomain.Contains("{dc}", StringComparison.OrdinalIgnoreCase)
                ? baseDomain.Replace("{dc}", info.DataCenter.ToString(), StringComparison.OrdinalIgnoreCase)
                : $"kws{info.DataCenter}.{baseDomain}";
            // A custom front domain is resolved by its own DNS; the Telegram DC IP is
            // only the target for the direct Telegram hostname candidates above.
            if (options.CloudflarePriority)
                candidates.Insert(0, (cfHost, cfHost));
            else
                candidates.Add((cfHost, cfHost));
        }

        if (options.CloudflareEnabled)
        {
            foreach (string workerDomain in options.CloudflareWorkerDomains)
            {
                string workerHost = workerDomain.Trim().TrimEnd('.');
                if (string.IsNullOrWhiteSpace(workerHost)) continue;
                if (options.CloudflarePriority)
                    candidates.Insert(0, (workerHost, workerHost));
                else
                    candidates.Add((workerHost, workerHost));
            }
        }
        return candidates;
    }

    private static string ResolveDataCenterTarget(int dataCenter, TgWsProxyOptions options)
    {
        if (options.DataCenterTargets.TryGetValue(dataCenter, out string? configured)
            && !string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
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

    public void Dispose() => Stop();
}