using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohProviderSwitchServiceTests
{
    [Fact]
    public async Task SwitchAsync_WhenDhcpResetSucceeds_AppliesNewProviderAfterReset()
    {
        // Arrange
        var sequence = new MockSequence();
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.InSequence(sequence)
            .Setup(x => x.DisableAsync("Wi-Fi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "DHCP"));
        configuration.InSequence(sequence)
            .Setup(x => x.ApplyAsync(
                "Wi-Fi", It.Is<DohProvider>(provider => provider.Id == "google"),
                DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "Google включён"));
        var service = new DohProviderSwitchService(configuration.Object);

        // Act
        var result = await service.SwitchAsync(
            "Wi-Fi", CreateProvider("google"), DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.AppliedNew, result.SwitchOutcome);
        Assert.Equal("Google включён", result.Message);
        configuration.VerifyAll();
    }

    [Fact]
    public async Task SwitchAsync_WhenDhcpResetFails_DoesNotApplyNewProvider()
    {
        // Arrange
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.DisableAsync("Wi-Fi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "DHCP недоступен"));
        var service = new DohProviderSwitchService(configuration.Object);

        // Act
        var result = await service.SwitchAsync(
            "Wi-Fi", CreateProvider("google"), DohEncryptionMode.EncryptedWithFallback);

        // Assert
        Assert.False(result.IsSuccess);
        configuration.Verify(x => x.ApplyAsync(
            It.IsAny<string>(), It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchAsync_WhenNewProviderFails_RestoresPreviousProviderWithCleanupToken()
    {
        var previous = CreateProvider("cloudflare");
        var next = CreateProvider("google");
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.DisableAsync("Wi-Fi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "DHCP"));
        configuration.Setup(x => x.ApplyAsync("Wi-Fi", next, DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "new failed"));
        configuration.Setup(x => x.ApplyAsync("Wi-Fi", previous, DohEncryptionMode.EncryptedWithFallback, CancellationToken.None))
            .ReturnsAsync(new DohApplyResult(true, "restored"));
        var service = new DohProviderSwitchService(configuration.Object);

        var result = await service.SwitchAsync("Wi-Fi", previous, DohEncryptionMode.EncryptedWithFallback,
            next, DohEncryptionMode.EncryptedOnly);

        Assert.False(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.RestoredPrevious, result.SwitchOutcome);
        Assert.Contains("восстановлен", result.Message, StringComparison.OrdinalIgnoreCase);
        configuration.VerifyAll();
    }

    [Fact]
    public async Task SwitchAsync_WhenPreviousProviderRestoreFails_ReturnsTypedFailure()
    {
        var previous = CreateProvider("cloudflare");
        var next = CreateProvider("google");
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.DisableAsync("Wi-Fi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "DHCP"));
        configuration.Setup(x => x.ApplyAsync("Wi-Fi", next, DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "new failed"));
        configuration.Setup(x => x.ApplyAsync("Wi-Fi", previous, DohEncryptionMode.EncryptedWithFallback, CancellationToken.None))
            .ReturnsAsync(new DohApplyResult(false, "restore failed"));

        var result = await new DohProviderSwitchService(configuration.Object).SwitchAsync(
            "Wi-Fi", previous, DohEncryptionMode.EncryptedWithFallback, next, DohEncryptionMode.EncryptedOnly);

        Assert.False(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.RestoreFailed, result.SwitchOutcome);
        Assert.Contains("restore failed", result.Message);
    }

    private static DohProvider CreateProvider(string id) => new(
        id,
        id,
        new Uri($"https://{id}.example/dns-query"),
        ["8.8.8.8"],
        true);
}
