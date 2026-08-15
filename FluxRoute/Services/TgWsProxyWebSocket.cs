using System.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FluxRoute.Services;

/// <summary>Small WebSocket-over-TLS transport used by the embedded proxy.</summary>
internal sealed class TgWsSocket : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const ulong MaxFrameBytes = 16 * 1024 * 1024;
    private readonly TcpClient _tcp;
    private readonly SslStream _tls;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _closed;

    private TgWsSocket(TcpClient tcp, SslStream tls)
    {
        _tcp = tcp;
        _tls = tls;
    }

    internal static async Task<TgWsSocket> ConnectAsync(
        string targetHost,
        string hostHeader,
        string sniHost,
        string path,
        int bufferSize,
        bool preferIPv4,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var ct = timeoutCts.Token;

        var tcp = preferIPv4 ? new TcpClient(AddressFamily.InterNetwork) : new TcpClient();
        tcp.NoDelay = true;
        tcp.ReceiveBufferSize = bufferSize;
        tcp.SendBufferSize = bufferSize;
        try
        {
            await tcp.ConnectAsync(targetHost, 443, ct);
            var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = sniHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);

            var socket = new TgWsSocket(tcp, tls);
            await socket.HandshakeAsync(hostHeader, path, ct);
            return socket;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    internal async Task SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_closed) throw new IOException("WebSocket is closed.");
            byte[] frame = BuildFrame(0x2, payload.Span, mask: true);
            await _tls.WriteAsync(frame, cancellationToken);
            await _tls.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    internal async Task<byte[]?> ReceiveDataAsync(CancellationToken cancellationToken)
    {
        while (!_closed)
        {
            (byte opcode, bool final, byte[] payload) = await ReadFrameAsync(cancellationToken);
            if (opcode == 0x9)
            {
                await SendControlAsync(0xA, payload, cancellationToken);
                continue;
            }
            if (opcode == 0xA)
                continue;
            if (opcode == 0x8)
            {
                await SendControlAsync(0x8, payload.Length >= 2 ? payload.AsMemory(0, 2) : ReadOnlyMemory<byte>.Empty, cancellationToken);
                _closed = true;
                return null;
            }
            if (opcode is not (0x1 or 0x2 or 0x0))
                continue;

            if (opcode is 0x1 or 0x2)
            {
                if (final) return payload;
                var fragments = new List<byte[]>([payload]);
                int total = payload.Length;
                while (true)
                {
                    var next = await ReadFrameAsync(cancellationToken);
                    if (next.opcode == 0x9)
                    {
                        await SendControlAsync(0xA, next.payload, cancellationToken);
                        continue;
                    }
                    if (next.opcode != 0x0)
                        throw new IOException("Invalid fragmented WebSocket message.");
                    fragments.Add(next.payload);
                    total = checked(total + next.payload.Length);
                    if (next.final)
                    {
                        var message = new byte[total];
                        int offset = 0;
                        foreach (var part in fragments)
                        {
                            part.CopyTo(message, offset);
                            offset += part.Length;
                        }
                        return message;
                    }
                }
            }
        }
        return null;
    }

    private async Task HandshakeAsync(string hostHeader, string path, CancellationToken ct)
    {
        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        string request = $"GET {path} HTTP/1.1\r\n"
                       + $"Host: {hostHeader}\r\n"
                       + "Upgrade: websocket\r\n"
                       + "Connection: Upgrade\r\n"
                       + $"Sec-WebSocket-Key: {key}\r\n"
                       + "Sec-WebSocket-Version: 13\r\n"
                       + "Sec-WebSocket-Protocol: binary\r\n"
                       + "\r\n";
        await _tls.WriteAsync(Encoding.ASCII.GetBytes(request), ct);
        await _tls.FlushAsync(ct);

        byte[] response = await ReadHttpHeadersAsync(ct);
        string[] lines = Encoding.ASCII.GetString(response)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            throw new InvalidDataException("Empty WebSocket handshake response.");

        string[] statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out int status) || status != 101)
            throw new InvalidOperationException($"WebSocket upstream returned: {lines[0]}");

        string? accept = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();
        string expected = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
            key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        if (!string.Equals(accept, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid WebSocket accept key from upstream.");
    }
    private async Task<byte[]> ReadHttpHeadersAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] one = new byte[1];
        while (buffer.Length < MaxHeaderBytes)
        {
            int read = await _tls.ReadAsync(one, ct);
            if (read == 0) throw new EndOfStreamException("Upstream closed during WebSocket handshake.");
            buffer.WriteByte(one[0]);
            if (buffer.Length >= 4)
            {
                byte[] data = buffer.GetBuffer();
                int n = checked((int)buffer.Length);
                if (data[n - 4] == '\r' && data[n - 3] == '\n' && data[n - 2] == '\r' && data[n - 1] == '\n')
                    return buffer.ToArray();
            }
        }
        throw new InvalidDataException("WebSocket handshake headers are too large.");
    }

    private async Task<(byte opcode, bool final, byte[] payload)> ReadFrameAsync(CancellationToken ct)
    {
        byte[] header = new byte[2];
        await ReadExactlyAsync(_tls, header, ct);
        bool final = (header[0] & 0x80) != 0;
        byte opcode = (byte)(header[0] & 0x0F);
        bool masked = (header[1] & 0x80) != 0;
        ulong length = (uint)(header[1] & 0x7F);

        if (length == 126)
        {
            byte[] ext = new byte[2];
            await ReadExactlyAsync(_tls, ext, ct);
            length = BinaryPrimitives.ReadUInt16BigEndian(ext);
        }
        else if (length == 127)
        {
            byte[] ext = new byte[8];
            await ReadExactlyAsync(_tls, ext, ct);
            length = BinaryPrimitives.ReadUInt64BigEndian(ext);
        }

        if (length > MaxFrameBytes || length > int.MaxValue)
            throw new InvalidDataException("WebSocket frame is too large.");

        byte[] mask = masked ? new byte[4] : Array.Empty<byte>();
        if (masked) await ReadExactlyAsync(_tls, mask, ct);
        byte[] payload = new byte[(int)length];
        if (payload.Length > 0) await ReadExactlyAsync(_tls, payload, ct);
        if (masked)
        {
            for (int i = 0; i < payload.Length; i++)
                payload[i] ^= mask[i & 3];
        }
        return (opcode, final, payload);
    }

    private async Task SendControlAsync(byte opcode, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_closed) return;
            byte[] frame = BuildFrame(opcode, payload.Span, mask: true);
            await _tls.WriteAsync(frame, ct);
            await _tls.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static byte[] BuildFrame(byte opcode, ReadOnlySpan<byte> payload, bool mask)
    {
        if ((ulong)payload.Length > MaxFrameBytes)
            throw new ArgumentOutOfRangeException(nameof(payload));

        int extra = payload.Length < 126 ? 0 : payload.Length <= ushort.MaxValue ? 2 : 8;
        int prefix = 2 + extra + (mask ? 4 : 0);
        var frame = new byte[prefix + payload.Length];
        frame[0] = (byte)(0x80 | (opcode & 0x0F));
        int offset = 2;
        if (payload.Length < 126)
            frame[1] = (byte)(payload.Length | (mask ? 0x80 : 0));
        else if (payload.Length <= ushort.MaxValue)
        {
            frame[1] = (byte)(126 | (mask ? 0x80 : 0));
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), (ushort)payload.Length);
            offset += 2;
        }
        else
        {
            frame[1] = (byte)(127 | (mask ? 0x80 : 0));
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(offset, 8), (ulong)payload.Length);
            offset += 8;
        }

        if (mask)
        {
            Span<byte> maskKey = frame.AsSpan(offset, 4);
            RandomNumberGenerator.Fill(maskKey);
            offset += 4;
            for (int i = 0; i < payload.Length; i++)
                frame[offset + i] = (byte)(payload[i] ^ maskKey[i & 3]);
        }
        else payload.CopyTo(frame.AsSpan(offset));
        return frame;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) throw new EndOfStreamException();
            buffer = buffer[read..];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_closed)
        {
            try { await SendControlAsync(0x8, ReadOnlyMemory<byte>.Empty, CancellationToken.None); }
            catch { }
            _closed = true;
        }
        _sendLock.Dispose();
        await _tls.DisposeAsync();
        _tcp.Dispose();
    }
}