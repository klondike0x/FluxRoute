using System.Buffers.Binary;
using System.Security.Cryptography;
using FluxRoute.Services;

namespace FluxRoute.Core.Tests;

public sealed class TgWsProxyProtocolTests
{
    [Fact]
    public void DecodeHandshake_RecoversTransportAndDataCenter()
    {
        byte[] secret = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        byte[] clientMaterial = RandomNumberGenerator.GetBytes(48);
        byte[] plain = RandomNumberGenerator.GetBytes(64);
        clientMaterial.CopyTo(plain, 8);
        plain[56] = 0xEE;
        plain[57] = 0xEE;
        plain[58] = 0xEE;
        plain[59] = 0xEE;
        BinaryPrimitives.WriteInt16LittleEndian(plain.AsSpan(60, 2), -2);

        byte[] key = TgWsProxyProtocol.Sha256(clientMaterial.AsSpan(0, 32), secret);
        byte[] encrypted = new byte[64];
        using (var cipher = new AesCtr(key, clientMaterial.AsSpan(32, 16)))
            cipher.Transform(plain, encrypted);

        clientMaterial.CopyTo(encrypted, 8);
        Assert.True(TgWsProxyProtocol.TryDecodeHandshake(encrypted, secret, out var info));
        Assert.NotNull(info);
        Assert.Equal(2, info!.DataCenter);
        Assert.True(info.IsMedia);
        Assert.Equal(TgWsProxyProtocol.IntermediateTransport, info.TransportWord);
    }

    [Fact]
    public void PacketSplitter_PreservesEncryptedPacketBoundaries()
    {
        byte[] relay = TgWsProxyProtocol.CreateRelayHandshake(TgWsProxyProtocol.IntermediateTag, 2);
        byte[] plain = new byte[4 + 20];
        BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(0, 4), 20);
        RandomNumberGenerator.Fill(plain.AsSpan(4));
        byte[] encrypted;
        using (var cipher = new AesCtr(relay.AsSpan(8, 32), relay.AsSpan(40, 16)))
        {
            cipher.TransformZeros(64);
            encrypted = cipher.Transform(plain);
        }

        var splitter = new TgPacketSplitter(relay, TgWsProxyProtocol.IntermediateTransport);
        var first = splitter.Push(encrypted.AsSpan(0, 7));
        Assert.Empty(first);
        var second = splitter.Push(encrypted.AsSpan(7));
        Assert.Single(second);
        Assert.Equal(encrypted, second[0]);
    }

    [Fact]
    public void DecodeHandshake_PreservesSpecialDataCenter203ForRelay()
    {
        byte[] secret = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        byte[] clientMaterial = RandomNumberGenerator.GetBytes(48);
        byte[] plain = RandomNumberGenerator.GetBytes(64);
        clientMaterial.CopyTo(plain, 8);
        TgWsProxyProtocol.IntermediateTag.CopyTo(plain, 56);
        BinaryPrimitives.WriteInt16LittleEndian(plain.AsSpan(60, 2), 203);

        byte[] key = TgWsProxyProtocol.Sha256(clientMaterial.AsSpan(0, 32), secret);
        byte[] encrypted = new byte[64];
        using (var cipher = new AesCtr(key, clientMaterial.AsSpan(32, 16)))
            cipher.Transform(plain, encrypted);
        clientMaterial.CopyTo(encrypted, 8);

        Assert.True(TgWsProxyProtocol.TryDecodeHandshake(encrypted, secret, out var info));
        Assert.NotNull(info);
        Assert.Equal(203, info!.DataCenter);
        Assert.False(info.IsTest);
        Assert.Equal(203, info.WireDataCenter);
    }

    [Fact]
    public void DecodeHandshake_MapsTestDataCenterForRoutingButKeepsTestFlag()
    {
        byte[] secret = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        byte[] clientMaterial = RandomNumberGenerator.GetBytes(48);
        byte[] plain = RandomNumberGenerator.GetBytes(64);
        clientMaterial.CopyTo(plain, 8);
        TgWsProxyProtocol.IntermediateTag.CopyTo(plain, 56);
        BinaryPrimitives.WriteInt16LittleEndian(plain.AsSpan(60, 2), 10002);

        byte[] key = TgWsProxyProtocol.Sha256(clientMaterial.AsSpan(0, 32), secret);
        byte[] encrypted = new byte[64];
        using (var cipher = new AesCtr(key, clientMaterial.AsSpan(32, 16)))
            cipher.Transform(plain, encrypted);
        clientMaterial.CopyTo(encrypted, 8);

        Assert.True(TgWsProxyProtocol.TryDecodeHandshake(encrypted, secret, out var info));
        Assert.NotNull(info);
        Assert.Equal(2, info!.DataCenter);
        Assert.True(info.IsTest);
        Assert.Equal(2, info.WireDataCenter);
    }
}