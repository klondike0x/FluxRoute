using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace FluxRoute.Services;

/// <summary>
/// Low-level protocol primitives for the embedded Telegram MTProto-over-WebSocket bridge.
/// The implementation is intentionally self-contained so the application does not need a
/// Python runtime or a second process.
/// </summary>
internal static class TgWsProxyProtocol
{
    internal const int HandshakeSize = 64;
    internal const int KeyMaterialOffset = 8;
    internal const int KeyMaterialSize = 48;
    internal const int PreKeySize = 32;
    internal const int IvSize = 16;
    internal const int ProtocolOffset = 56;
    internal const int DcOffset = 60;

    internal static readonly byte[] AbridgedTag = { 0xEF, 0xEF, 0xEF, 0xEF };
    internal static readonly byte[] IntermediateTag = { 0xEE, 0xEE, 0xEE, 0xEE };
    internal static readonly byte[] SecureTag = { 0xDD, 0xDD, 0xDD, 0xDD };

    internal const uint AbridgedTransport = 0xEFEFEFEF;
    internal const uint IntermediateTransport = 0xEEEEEEEE;
    internal const uint SecureTransport = 0xDDDDDDDD;

    private static readonly byte[][] ReservedStarts =
    [
        [0x48, 0x45, 0x41, 0x44], // HEAD
        [0x50, 0x4F, 0x53, 0x54], // POST
        [0x47, 0x45, 0x54, 0x20], // GET
        [0xEE, 0xEE, 0xEE, 0xEE],
        [0xDD, 0xDD, 0xDD, 0xDD],
        [0x16, 0x03, 0x01, 0x02]
    ];

    internal sealed record HandshakeInfo(
        int DataCenter,
        bool IsMedia,
        bool IsTest,
        int WireDataCenter,
        byte[] ProtocolTag,
        uint TransportWord,
        byte[] ClientKeyMaterial);

    internal sealed class CryptoBridge : IDisposable
    {
        internal CryptoBridge(AesCtr clientDecrypt, AesCtr clientEncrypt,
            AesCtr upstreamEncrypt, AesCtr upstreamDecrypt)
        {
            ClientDecrypt = clientDecrypt;
            ClientEncrypt = clientEncrypt;
            UpstreamEncrypt = upstreamEncrypt;
            UpstreamDecrypt = upstreamDecrypt;
        }

        internal AesCtr ClientDecrypt { get; }
        internal AesCtr ClientEncrypt { get; }
        internal AesCtr UpstreamEncrypt { get; }
        internal AesCtr UpstreamDecrypt { get; }

        public void Dispose()
        {
            ClientDecrypt.Dispose();
            ClientEncrypt.Dispose();
            UpstreamEncrypt.Dispose();
            UpstreamDecrypt.Dispose();
        }
    }

    internal static bool TryDecodeHandshake(ReadOnlySpan<byte> handshake, ReadOnlySpan<byte> secret,
        out HandshakeInfo? result)
    {
        result = null;
        if (handshake.Length != HandshakeSize || secret.Length != 16)
            return false;

        var keyMaterial = handshake.Slice(KeyMaterialOffset, KeyMaterialSize).ToArray();
        var key = Sha256(keyMaterial.AsSpan(0, PreKeySize), secret);
        var iv = keyMaterial.AsSpan(PreKeySize, IvSize).ToArray();
        var plain = new byte[HandshakeSize];
        using (var cipher = new AesCtr(key, iv))
            cipher.Transform(handshake, plain);

        var tag = plain.AsSpan(ProtocolOffset, 4).ToArray();
        uint transport;
        if (SequenceEqual(tag, AbridgedTag)) transport = AbridgedTransport;
        else if (SequenceEqual(tag, IntermediateTag)) transport = IntermediateTransport;
        else if (SequenceEqual(tag, SecureTag)) transport = SecureTransport;
        else return false;

        short rawDc = BinaryPrimitives.ReadInt16LittleEndian(plain.AsSpan(DcOffset, 2));
        int absoluteDc = Math.Abs(rawDc);
        if (absoluteDc == 0)
            return false;

        bool isMedia = rawDc < 0;
        bool isTest = absoluteDc is >= 10001 and <= 10003;
        int dataCenter = isTest ? absoluteDc - 10000 : NormalizeDataCenter(absoluteDc);
        if (dataCenter is < 1 or > 5)
            return false;

        int wireDc = isMedia ? -dataCenter : dataCenter;
        result = new HandshakeInfo(dataCenter, isMedia, isTest, wireDc,
            tag, transport, keyMaterial);
        return true;
    }

    internal static byte[] CreateRelayHandshake(ReadOnlySpan<byte> protocolTag, int wireDataCenter)
    {
        if (protocolTag.Length != 4)
            throw new ArgumentException("Protocol tag must contain four bytes.", nameof(protocolTag));

        byte[] seed;
        do
        {
            seed = RandomNumberGenerator.GetBytes(HandshakeSize);
        }
        while (seed[0] == 0xEF
               || ReservedStarts.Any(prefix => seed.AsSpan(0, 4).SequenceEqual(prefix))
               || (seed[4] == 0 && seed[5] == 0 && seed[6] == 0 && seed[7] == 0));

        byte[] key = seed.AsSpan(KeyMaterialOffset, PreKeySize).ToArray();
        byte[] iv = seed.AsSpan(KeyMaterialOffset + PreKeySize, IvSize).ToArray();
        byte[] encrypted = new byte[HandshakeSize];
        using (var cipher = new AesCtr(key, iv))
            cipher.Transform(seed, encrypted);

        Span<byte> tail = stackalloc byte[8];
        protocolTag.CopyTo(tail);
        BinaryPrimitives.WriteInt16LittleEndian(tail.Slice(4, 2), checked((short)wireDataCenter));
        RandomNumberGenerator.Fill(tail.Slice(6, 2));

        for (int i = 0; i < tail.Length; i++)
            seed[ProtocolOffset + i] = (byte)(tail[i] ^ (encrypted[ProtocolOffset + i] ^ seed[ProtocolOffset + i]));

        return seed;
    }

    internal static CryptoBridge CreateCrypto(ReadOnlySpan<byte> clientMaterial,
        ReadOnlySpan<byte> secret, ReadOnlySpan<byte> relayHandshake)
    {
        if (clientMaterial.Length != KeyMaterialSize || relayHandshake.Length != HandshakeSize)
            throw new ArgumentException("Invalid key material length.");

        byte[] clientDecKey = Sha256(clientMaterial[..PreKeySize], secret);
        byte[] clientDecIv = clientMaterial[PreKeySize..].ToArray();

        byte[] reversedClient = clientMaterial.ToArray();
        Array.Reverse(reversedClient);
        byte[] clientEncKey = Sha256(reversedClient.AsSpan(0, PreKeySize), secret);
        byte[] clientEncIv = reversedClient.AsSpan(PreKeySize, IvSize).ToArray();

        byte[] upstreamEncKey = relayHandshake.Slice(KeyMaterialOffset, PreKeySize).ToArray();
        byte[] upstreamEncIv = relayHandshake.Slice(KeyMaterialOffset + PreKeySize, IvSize).ToArray();

        byte[] reversedRelay = relayHandshake.Slice(KeyMaterialOffset, KeyMaterialSize).ToArray();
        Array.Reverse(reversedRelay);
        byte[] upstreamDecKey = reversedRelay.AsSpan(0, PreKeySize).ToArray();
        byte[] upstreamDecIv = reversedRelay.AsSpan(PreKeySize, IvSize).ToArray();

        var clientDecrypt = new AesCtr(clientDecKey, clientDecIv);
        var clientEncrypt = new AesCtr(clientEncKey, clientEncIv);
        var upstreamEncrypt = new AesCtr(upstreamEncKey, upstreamEncIv);
        var upstreamDecrypt = new AesCtr(upstreamDecKey, upstreamDecIv);

        // The client and the upstream have already consumed their 64-byte init packets.
        clientDecrypt.TransformZeros(HandshakeSize);
        upstreamEncrypt.TransformZeros(HandshakeSize);

        return new CryptoBridge(clientDecrypt, clientEncrypt, upstreamEncrypt, upstreamDecrypt);
    }

    internal static int NormalizeDataCenter(int dataCenter) => dataCenter == 203 ? 2 : dataCenter;

    internal static byte[] Sha256(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        byte[] buffer = new byte[first.Length + second.Length];
        first.CopyTo(buffer);
        second.CopyTo(buffer.AsSpan(first.Length));
        return SHA256.HashData(buffer);
    }

    private static bool SequenceEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceEqual(b);
}

internal sealed class AesCtr : IDisposable
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;
    private readonly byte[] _counter = new byte[16];
    private readonly byte[] _streamBlock = new byte[16];
    private int _streamOffset = 16;

    internal AesCtr(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
    {
        if (key.Length != 32 || iv.Length != 16)
            throw new ArgumentException("AES-CTR requires a 32-byte key and a 16-byte IV.");

        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = key.ToArray();
        _encryptor = _aes.CreateEncryptor();
        iv.CopyTo(_counter);
    }

    internal byte[] Transform(ReadOnlySpan<byte> input)
    {
        var output = new byte[input.Length];
        Transform(input, output);
        return output;
    }

    internal void Transform(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("Output buffer is too small.", nameof(output));

        for (int i = 0; i < input.Length; i++)
        {
            if (_streamOffset == 16)
            {
                _encryptor.TransformBlock(_counter, 0, 16, _streamBlock, 0);
                IncrementCounter();
                _streamOffset = 0;
            }
            output[i] = (byte)(input[i] ^ _streamBlock[_streamOffset++]);
        }
    }

    internal void TransformZeros(int count)
    {
        if (count <= 0) return;
        Span<byte> zeros = count <= 4096 ? stackalloc byte[count] : new byte[count];
        Transform(zeros, zeros);
    }

    private void IncrementCounter()
    {
        for (int i = _counter.Length - 1; i >= 0; i--)
        {
            if (++_counter[i] != 0) break;
        }
    }

    public void Dispose()
    {
        _encryptor.Dispose();
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_counter);
        CryptographicOperations.ZeroMemory(_streamBlock);
    }
}

internal sealed class TgPacketSplitter
{
    private readonly AesCtr _plainDecoder;
    private readonly uint _transport;
    private readonly List<byte> _ciphertext = new();
    private readonly List<byte> _plaintext = new();
    private bool _passthrough;

    internal TgPacketSplitter(ReadOnlySpan<byte> relayHandshake, uint transport)
    {
        _transport = transport;
        byte[] key = relayHandshake.Slice(8, 32).ToArray();
        byte[] iv = relayHandshake.Slice(40, 16).ToArray();
        _plainDecoder = new AesCtr(key, iv);
        _plainDecoder.TransformZeros(64);
    }

    internal List<byte[]> Push(ReadOnlySpan<byte> encryptedChunk)
    {
        var result = new List<byte[]>();
        if (encryptedChunk.Length == 0) return result;
        if (_passthrough)
        {
            result.Add(encryptedChunk.ToArray());
            return result;
        }

        _ciphertext.AddRange(encryptedChunk.ToArray());
        _plaintext.AddRange(_plainDecoder.Transform(encryptedChunk));

        int offset = 0;
        while (offset < _ciphertext.Count)
        {
            int? length = TryPacketLength(offset, _ciphertext.Count - offset);
            if (length is null) break;
            if (length <= 0)
            {
                result.Add(_ciphertext.Skip(offset).ToArray());
                offset = _ciphertext.Count;
                _passthrough = true;
                break;
            }

            result.Add(_ciphertext.Skip(offset).Take(length.Value).ToArray());
            offset += length.Value;
        }

        if (offset > 0)
        {
            _ciphertext.RemoveRange(0, offset);
            _plaintext.RemoveRange(0, offset);
        }
        return result;
    }

    internal List<byte[]> Flush()
    {
        var result = new List<byte[]>();
        if (_ciphertext.Count > 0)
            result.Add(_ciphertext.ToArray());
        _ciphertext.Clear();
        _plaintext.Clear();
        return result;
    }

    private int? TryPacketLength(int offset, int available)
    {
        if (_transport == TgWsProxyProtocol.AbridgedTransport)
        {
            if (available < 1) return null;
            int first = _plaintext[offset];
            int header = 1;
            int words;
            if (first is 0x7F or 0xFF)
            {
                if (available < 4) return null;
                words = _plaintext[offset + 1]
                      | (_plaintext[offset + 2] << 8)
                      | (_plaintext[offset + 3] << 16);
                header = 4;
            }
            else words = first & 0x7F;
            int payload = checked(words * 4);
            return payload <= 0 ? 0 : header + payload <= available ? header + payload : null;
        }

        if (_transport is TgWsProxyProtocol.IntermediateTransport or TgWsProxyProtocol.SecureTransport)
        {
            if (available < 4) return null;
            uint payload = BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshal.AsSpan(_plaintext).Slice(offset, 4)) & 0x7FFFFFFF;
            if (payload == 0) return 0;
            long total = 4L + payload;
            return total > int.MaxValue ? 0 : total <= available ? (int)total : null;
        }

        return 0;
    }
}