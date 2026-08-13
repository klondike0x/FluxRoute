using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohProviderSwitchServiceTests
{
    [Fact]
    public async Task SwitchAsync_AppliesNewProviderDirectlyWithoutDhcpReset()
    {
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.ApplyAsync(
                "Wi-Fi", It.Is<DohProvider>(provider => provider.Id == "google"),
                DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "Google applied"));
        var service = new DohProviderSwitchService(configuration.Object);

        var result = await service.SwitchAsync(
            "Wi-Fi", CreateProvider("xbox"), DohEncryptionMode.EncryptedOnly,
            CreateProvider("google"), DohEncryptionMode.EncryptedOnly);

        Assert.True(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.AppliedNew, result.SwitchOutcome);
        Assert.Equal("Google applied", result.Message);
        configuration.Verify(x => x.DisableAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchAsync_WhenApplyFails_PreservesPreviousState()
    {
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.ApplyAsync(
                "Wi-Fi", It.Is<DohProvider>(provider => provider.Id == "google"),
                DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "new failed"));
        var service = new DohProviderSwitchService(configuration.Object);

        var result = await service.SwitchAsync(
            "Wi-Fi", CreateProvider("xbox"), DohEncryptionMode.EncryptedOnly,
            CreateProvider("google"), DohEncryptionMode.EncryptedOnly);

        Assert.False(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.RestoredPrevious, result.SwitchOutcome);
        Assert.Contains("new failed", result.Message, StringComparison.OrdinalIgnoreCase);
        configuration.Verify(x => x.DisableAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchAsync_WhenApplyReportsRestoreFailure_ReturnsTypedFailure()
    {
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.ApplyAsync(
                "Wi-Fi", It.Is<DohProvider>(provider => provider.Id == "google"),
                DohEncryptionMode.EncryptedOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(
                false, "rollback failed", DohProviderSwitchOutcome.RestoreFailed));
        var service = new DohProviderSwitchService(configuration.Object);

        var result = await service.SwitchAsync(
            "Wi-Fi", CreateProvider("xbox"), DohEncryptionMode.EncryptedOnly,
            CreateProvider("google"), DohEncryptionMode.EncryptedOnly);

        Assert.False(result.IsSuccess);
        Assert.Equal(DohProviderSwitchOutcome.RestoreFailed, result.SwitchOutcome);
        Assert.Contains("rollback failed", result.Message);
        configuration.Verify(x => x.DisableAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DohProvider CreateProvider(string id) => new(
        id,
        id,
        new Uri($"https://{id}.example/dns-query"),
        ["8.8.8.8"],
        true);
}
