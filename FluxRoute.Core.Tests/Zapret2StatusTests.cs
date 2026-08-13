using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class Zapret2StatusTests
{
    [Fact]
    public void Evaluator_UserStopped_ReturnsStopped()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput() with { UserStopped = true });

        Assert.Equal(ProtectionStatus.Stopped, status);
    }

    [Fact]
    public void Evaluator_AllChecksPassed_ReturnsHealthy()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput());

        Assert.Equal(ProtectionStatus.Healthy, status);
    }

    [Fact]
    public void Evaluator_PartialConnectivity_ReturnsDegraded()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput() with { ConnectionHealthy = false });

        Assert.Equal(ProtectionStatus.Degraded, status);
    }

    [Fact]
    public void Evaluator_WinDivertUnavailable_ReturnsError()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput() with { WinDivertAvailable = false });

        Assert.Equal(ProtectionStatus.Error, status);
    }

    [Fact]
    public void Evaluator_ProcessFailure_ReturnsError()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput() with { Winws2Running = false });

        Assert.Equal(ProtectionStatus.Error, status);
    }

    [Fact]
    public void Evaluator_ErrorToRepairing_IsExplicit()
    {
        var status = ProtectionStatusEvaluator.Evaluate(HealthyInput() with
        {
            IsRepairing = true,
            Winws2Running = false,
            WinDivertAvailable = false
        });

        Assert.Equal(ProtectionStatus.Repairing, status);
    }

    [Fact]
    public async Task StatusService_StartFailure_PublishesError()
    {
        var host = new Mock<IZapret2ProcessHost>();
        host.Setup(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(false, "winws2 завершился с ошибкой."));
        var service = CreateService(host);

        var result = await service.StartAsync(CreateStartRequest());

        Assert.False(result.Success);
        Assert.Equal(ProtectionStatus.Error, service.Current.Status);
        Assert.Contains("завершился", service.Current.DiagnosticMessage);
    }

    [Fact]
    public async Task StatusService_RepairSuccess_ReturnsSuccess()
    {
        var host = new Mock<IZapret2ProcessHost>();
        host.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Остановлен."));
        host.Setup(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Запущен.", 42));
        var service = CreateService(host);

        var result = await service.RepairAsync(
            new Zapret2RepairRequest(CreateStartRequest(), RestartDelay: TimeSpan.Zero),
            _ => Task.FromResult(HealthySnapshot()));

        Assert.True(result.Success);
        Assert.Equal(42, result.ProcessId);
        host.Verify(x => x.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StatusService_RepairFailure_DoesNotSwitchProfileWithoutFallbackPermission()
    {
        var host = new Mock<IZapret2ProcessHost>();
        host.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Остановлен."));
        host.Setup(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(false, "Активный профиль не запустился."));
        var service = CreateService(host);

        var result = await service.RepairAsync(
            new Zapret2RepairRequest(
                CreateStartRequest(),
                new Zapret2StartRequest("fallback.exe", ".", [], "fallback"),
                AllowFallback: false,
                RestartDelay: TimeSpan.Zero),
            _ => Task.FromResult(Zapret2StatusSnapshot.Stopped("Проверка не пройдена.")));

        Assert.False(result.Success);
        host.Verify(x => x.StartAsync(
            It.Is<Zapret2StartRequest>(request => request.ActiveProfile == "fallback"),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(ProtectionStatus.Error, service.Current.Status);
    }

    [Fact]
    public async Task StatusService_RepairFailure_WithPermission_UsesFallback()
    {
        var host = new Mock<IZapret2ProcessHost>();
        host.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Остановлен."));
        host.SetupSequence(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(false, "Активный профиль не запустился."))
            .ReturnsAsync(new Zapret2OperationResult(true, "Fallback запущен.", 84));
        var service = CreateService(host);

        var result = await service.RepairAsync(
            new Zapret2RepairRequest(
                CreateStartRequest(),
                new Zapret2StartRequest("fallback.exe", ".", [], "fallback"),
                AllowFallback: true,
                RestartDelay: TimeSpan.Zero),
            _ => Task.FromResult(HealthySnapshot()));

        Assert.True(result.Success);
        Assert.Equal(84, result.ProcessId);
        host.Verify(x => x.StartAsync(
            It.Is<Zapret2StartRequest>(request => request.ActiveProfile == "fallback"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StatusService_Cancellation_IsPassedToProcessHost()
    {
        var host = new Mock<IZapret2ProcessHost>();
        var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();
        host.Setup(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), tokenSource.Token))
            .ReturnsAsync(new Zapret2OperationResult(false, "Отменено."));
        var service = CreateService(host);

        var result = await service.StartAsync(CreateStartRequest(), tokenSource.Token);

        Assert.False(result.Success);
        host.Verify(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), tokenSource.Token), Times.Once);
    }

    [Fact]
    public async Task RecoveryService_SafeSequence_PublishesRepairingAndStopsBeforeStart()
    {
        var host = new Mock<IZapret2ProcessHost>();
        var status = new Mock<IZapret2StatusService>();
        host.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Остановлен."));
        host.Setup(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Zapret2OperationResult(true, "Запущен.", 7));
        var service = new Zapret2RecoveryService(
            host.Object,
            status.Object,
            new Mock<ILogger<Zapret2RecoveryService>>().Object);

        var result = await service.RecoverAsync(
            new Zapret2RepairRequest(CreateStartRequest(), RestartDelay: TimeSpan.Zero),
            _ => Task.FromResult(HealthySnapshot()));

        Assert.True(result.Success);
        status.Verify(x => x.PublishOperationStatus(ProtectionStatus.Repairing, It.IsAny<string>()), Times.Once);
        host.Verify(x => x.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(x => x.StartAsync(It.IsAny<Zapret2StartRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    private static Zapret2StatusService CreateService(Mock<IZapret2ProcessHost> host) =>
        new(host.Object, new Mock<ILogger<Zapret2StatusService>>().Object);

    private static Zapret2HealthInput HealthyInput() =>
        new(
            UserStopped: false,
            IsStarting: false,
            IsRepairing: false,
            Winws2Running: true,
            WinDivertAvailable: true,
            ProfileLoaded: true,
            StrategyActive: true,
            ConnectionHealthy: true,
            CriticalLogErrors: false,
            TrafficMonitoringAvailable: true);

    private static Zapret2StatusSnapshot HealthySnapshot() =>
        new()
        {
            Status = ProtectionStatus.Healthy,
            Winws2Running = true,
            WinDivertAvailable = true,
            ProfileLoaded = true,
            StrategyActive = true,
            ConnectionHealthy = true,
            TrafficMonitoringAvailable = true
        };

    private static Zapret2StartRequest CreateStartRequest() =>
        new("winws2.exe", ".", ["--lua-init=@profile.lua"], "active");
}
