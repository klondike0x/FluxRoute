using FluxRoute.AI.Services;

namespace FluxRoute.Core.Tests;

public sealed class NetworkFingerprintProviderTests
{
    [Fact]
    public void Capture_ReturnsStableHexHash()
    {
        var p = new NetworkFingerprintProvider();
        var a = p.Capture();
        Assert.Equal(64, a.Hash.Length);
        Assert.All(a.Hash, c => Assert.True(Uri.IsHexDigit(c)));
    }
}
