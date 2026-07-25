using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohStartupRecoveryTests
{
    [Fact]
    public async Task RecoverAsync_InvokesPendingRecoveryBeforeStartupContinues()
    {
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.RecoverPendingOperationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "Восстановлено"));
        var recovery = new DohStartupRecovery(configuration.Object);

        await recovery.RecoverAsync();

        configuration.Verify(x => x.RecoverPendingOperationAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverAsync_WhenRecoveryFails_StopsStartup()
    {
        var configuration = new Mock<IWindowsDohConfigurationService>();
        configuration.Setup(x => x.RecoverPendingOperationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "rollback failed"));
        var recovery = new DohStartupRecovery(configuration.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverAsync());

        Assert.Contains("rollback failed", error.Message);
    }
}
