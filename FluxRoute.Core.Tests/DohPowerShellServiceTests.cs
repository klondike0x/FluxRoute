using System.Net;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohPowerShellServiceTests
{
    [Theory]
    [InlineData(DohEncryptionMode.EncryptedWithFallback, "True")]
    [InlineData(DohEncryptionMode.EncryptedOnly, "False")]
    public async Task ConfigureAsync_EncryptedMode_UsesStructuredDnsClientCmdlet(
        DohEncryptionMode mode,
        string expectedFallback)
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureAsync(CreateProvider(), mode);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg => arg.Contains(
                $"-AllowFallbackToUdp ${expectedFallback}", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureAsync_NewProvider_AddsMappingWhenItDoesNotExist()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureAsync(CreateProvider(), DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg =>
                arg.Contains("Get-DnsClientDohServerAddress", StringComparison.Ordinal)
                && arg.Contains("Add-DnsClientDohServerAddress", StringComparison.Ordinal)
                && arg.Contains("Set-DnsClientDohServerAddress", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureAsync_PowerShellScript_ForcesUtf8Output()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "Ошибка Windows"));
        var service = CreateService(runner);

        // Act
        _ = await service.ConfigureAsync(CreateProvider(), DohEncryptionMode.EncryptedOnly);

        // Assert
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg =>
                arg.Contains("OutputEncoding", StringComparison.Ordinal)
                && arg.Contains("UTF8Encoding", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WhenJsonMatchesFallbackMode_ReturnsTrue()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "[{\"ServerAddress\":\"1.1.1.1\",\"DohTemplate\":\"https://cloudflare-dns.com/dns-query\",\"AutoUpgrade\":true,\"AllowFallbackToUdp\":true}]",
                ""));
        var service = CreateService(runner);

        // Act
        var result = await service.VerifyAsync(CreateProvider(), DohEncryptionMode.EncryptedWithFallback);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAsync_WhenJsonHasWrongFallback_ReturnsFalse()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "[{\"ServerAddress\":\"1.1.1.1\",\"DohTemplate\":\"https://cloudflare-dns.com/dns-query\",\"AutoUpgrade\":true,\"AllowFallbackToUdp\":true}]",
                ""));
        var service = CreateService(runner);

        // Act
        var result = await service.VerifyAsync(CreateProvider(), DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ConfigureAdapterAsync_UsesDnsClientCmdletForSelectedInterface()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureAdapterAsync(
            "Беспроводная сеть", ["1.1.1.1", "1.0.0.1"]);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("ipv4")
                && args.Contains("address=1.1.1.1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureAdapterAsync_MixedFamilies_AppliesIpv4AndIpv6Separately()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureAdapterAsync(
            "Беспроводная сеть",
            ["111.88.96.50", "111.88.96.51", "2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"]);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("ipv4")
                && args.Contains("address=111.88.96.50")),
            It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("ipv6")
                && args.Contains("address=2a00:ab00:1233:26::50")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAdapterAsync_MixedFamilies_IgnoresOrderAndMatchesBothFamilies()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "[{\"AddressFamily\":23,\"ServerAddresses\":[\"2a00:ab00:1233:26::51\",\"2a00:ab00:1233:26::50\"]},"
                + "{\"AddressFamily\":2,\"ServerAddresses\":[\"111.88.96.51\",\"111.88.96.50\"]}]",
                ""));
        var service = CreateService(runner);

        // Act
        var result = await service.VerifyAdapterAsync(
            "Беспроводная сеть",
            ["111.88.96.50", "111.88.96.51", "2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"]);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAdapterAsync_WhenIpv4WasNotApplied_ReturnsFalse()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "[{\"AddressFamily\":2,\"ServerAddresses\":[\"192.168.0.1\"]},"
                + "{\"AddressFamily\":23,\"ServerAddresses\":[\"2a00:ab00:1233:26::50\",\"2a00:ab00:1233:26::51\"]}]",
                ""));
        var service = CreateService(runner);

        // Act
        var result = await service.VerifyAdapterAsync(
            "Беспроводная сеть",
            ["111.88.96.50", "111.88.96.51", "2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"]);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(DohEncryptionMode.EncryptedOnly, "17")]
    [InlineData(DohEncryptionMode.EncryptedWithFallback, "21")]
    public async Task ConfigureInterfaceModeAsync_EncryptedMode_WritesObservedWindowsFlags(
        DohEncryptionMode mode,
        string expectedFlags)
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureInterfaceModeAsync(
            "{2E61C098-C635-4D56-AB43-E41ED7033B99}", CreateProvider(), mode);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg =>
                arg.Contains("DohInterfaceSettings", StringComparison.Ordinal)
                && arg.Contains($"DohFlags -Value {expectedFlags}", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureInterfaceModeAsync_Unencrypted_RemovesInterfaceEntries()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);

        // Act
        var result = await service.ConfigureInterfaceModeAsync(
            "{2E61C098-C635-4D56-AB43-E41ED7033B99}", CreateProvider(), DohEncryptionMode.Unencrypted);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg =>
                arg.Contains("Remove-Item", StringComparison.Ordinal)
                && arg.Contains("1.1.1.1", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureInterfaceModeAsync_Ipv6_WritesDoh6Branch()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        var service = CreateService(runner);
        var provider = new DohProvider(
            "ipv6", "IPv6", new Uri("https://dns.example/dns-query"), ["2a00:ab00:1233:26::50"], true);

        // Act
        var result = await service.ConfigureInterfaceModeAsync(
            "{2E61C098-C635-4D56-AB43-E41ED7033B99}", provider, DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(args => args.Any(arg =>
                arg.Contains("DohInterfaceSettings\\Doh6\\2a00:ab00:1233:26::50", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyInterfaceModeAsync_ReadsBackTemplateAndFlags()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync("powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0,
                "[{\"Address\":\"1.1.1.1\",\"IsIpv6\":false,\"Exists\":true,\"Template\":\"https://cloudflare-dns.com/dns-query\",\"Flags\":17}]", ""));
        var service = CreateService(runner);

        var verified = await service.VerifyInterfaceModeAsync(
            "2E61C098-C635-4D56-AB43-E41ED7033B99", CreateProvider(), DohEncryptionMode.EncryptedOnly);

        Assert.True(verified);
    }

    [Theory]
    [InlineData("auto", DohGlobalMode.Automatic)]
    [InlineData("automatic", DohGlobalMode.Automatic)]
    [InlineData("yes", DohGlobalMode.Enabled)]
    [InlineData("enabled", DohGlobalMode.Enabled)]
    [InlineData("no", DohGlobalMode.Disabled)]
    [InlineData("disabled", DohGlobalMode.Disabled)]
    public async Task CaptureSystemStateAsync_NormalizedGlobalMode_ParsesKnownValue(
        string mode,
        DohGlobalMode expected)
    {
        var runner = CreateCaptureRunner($"{{\"Mode\":\"{mode}\"}}");
        var service = CreateService(runner);

        var result = await service.CaptureSystemStateAsync(
            "2E61C098-C635-4D56-AB43-E41ED7033B99", ["1.1.1.1"]);

        Assert.Equal(expected, result.GlobalDohMode);
    }

    [Fact]
    public async Task CaptureSystemStateAsync_UnknownGlobalMode_FailsClosed()
    {
        var service = CreateService(CreateCaptureRunner("{\"Mode\":\"неизвестно\"}"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CaptureSystemStateAsync(
                "2E61C098-C635-4D56-AB43-E41ED7033B99", ["1.1.1.1"]));

        Assert.Contains("глобальный режим DoH", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Mock<IProcessRunner> CreateCaptureRunner(string globalOutput)
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "powershell.exe",
                It.Is<IReadOnlyList<string>>(args => args.Last().Contains(
                    "dnsclient show global", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, globalOutput, ""));
        runner.Setup(x => x.RunAsync(
                "powershell.exe",
                It.Is<IReadOnlyList<string>>(args => !args.Last().Contains(
                    "dnsclient show global", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "[null]", ""));
        return runner;
    }

    private static DohPowerShellService CreateService(Mock<IProcessRunner> runner) => new(
        runner.Object,
        new Mock<ILogger<DohPowerShellService>>().Object);

    private static DohProvider CreateProvider() => new(
        "cloudflare",
        "Cloudflare",
        new Uri("https://cloudflare-dns.com/dns-query"),
        ["1.1.1.1"],
        true);
}
