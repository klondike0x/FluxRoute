using FluxRoute.Core.Services.Warp;
using System.Net.Http;
using Xunit;

namespace FluxRoute.Core.Tests;

public class WarpServiceTests
{
    [Fact]
    public async Task RegisterAsync_ReturnsValidConfig()
    {
        var service = new WarpService(new HttpClient());
        var config = await service.RegisterAsync();

        Assert.NotNull(config);
        Assert.NotEmpty(config.PrivateKey);
        Assert.NotEmpty(config.PublicKey);
        Assert.NotEmpty(config.AddressV4);
        Assert.NotEmpty(config.AddressV6);
    }

    [Fact]
    public void GenerateWireGuardConfig_ReturnsValidString()
    {
        var service = new WarpService(new HttpClient());
        var config = new WarpConfig
        {
            PrivateKey = "PRIV",
            PublicKey = "PUB",
            AddressV4 = "1.2.3.4/32",
            AddressV6 = "::1/128",
            Endpoint = "end:123"
        };

        var result = service.GenerateWireGuardConfig(config);

        Assert.Contains("[Interface]", result);
        Assert.Contains("PrivateKey = PRIV", result);
        Assert.Contains("Address = 1.2.3.4/32, ::1/128", result);
        Assert.Contains("[Peer]", result);
        Assert.Contains("PublicKey = PUB", result);
        Assert.Contains("Endpoint = end:123", result);
    }
}
