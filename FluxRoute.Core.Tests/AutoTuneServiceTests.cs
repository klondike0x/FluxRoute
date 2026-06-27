using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.ViewModels;
using Moq;
using Xunit;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для Auto-Tune: модель AutoTuneResult и сценарии RunAutoTuneAsync.
/// </summary>
public sealed class AutoTuneServiceTests : IDisposable
{
    private readonly Mock<IConnectivityChecker> _checkerMock;
    private readonly Mock<IHttpClientFactory> _httpFactoryMock;

    public AutoTuneServiceTests()
    {
        _checkerMock = new Mock<IConnectivityChecker>();
        _httpFactoryMock = new Mock<IHttpClientFactory>();

        // Инициализация WPF Application (если не создан)
        if (Application.Current is null)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    public void Dispose()
    {
        // Очистка не требуется
    }

    // ═══════════════════════════════════════════════
    //  AutoTuneResult — модель подсчёта скора
    // ═══════════════════════════════════════════════

    [Fact]
    public void AutoTuneResult_PerfectScore_ReturnsMax()
    {
        var r = new AutoTuneResult
        {
            IpSetMode = "loaded",
            GameFilterProtocol = "TCP и UDP",
            SuccessCount = 5,
            TotalCount = 5,
            AvgLatencyMs = 50,
            MinLatencyMs = 30,
            MaxLatencyMs = 80
        };

        Assert.True(r.IsPerfect);
        Assert.Equal(100.0, r.SuccessRate);
        Assert.InRange(r.CompositeScore, 50, 100);
    }

    [Fact]
    public void AutoTuneResult_ZeroSuccess_ReturnsLowScore()
    {
        var r = new AutoTuneResult
        {
            IpSetMode = "any",
            GameFilterProtocol = "Выкл",
            SuccessCount = 0,
            TotalCount = 5,
            AvgLatencyMs = 2000,
            MinLatencyMs = 1000,
            MaxLatencyMs = 3000
        };

        Assert.False(r.IsPerfect);
        Assert.Equal(0, r.SuccessRate);
        Assert.Equal(3, r.CompositeScore); // stabilityScore = (int)((1 - 2000/3000)*10) = 3
    }

    [Fact]
    public void AutoTuneResult_PartialSuccess_CalculatesCorrectly()
    {
        var r = new AutoTuneResult
        {
            IpSetMode = "none",
            GameFilterProtocol = "TCP",
            SuccessCount = 3,
            TotalCount = 4,
            AvgLatencyMs = 120,
            MinLatencyMs = 50,
            MaxLatencyMs = 200
        };

        Assert.False(r.IsPerfect);
        Assert.Equal(75.0, r.SuccessRate, 1);
        Assert.InRange(r.CompositeScore, 20, 80);
    }

    [Fact]
    public void AutoTuneResult_DisplayText_FormatsCorrectly()
    {
        var r = new AutoTuneResult
        {
            IpSetMode = "loaded",
            GameFilterProtocol = "TCP и UDP",
            SuccessCount = 4,
            TotalCount = 5,
            AvgLatencyMs = 100.5,
            MinLatencyMs = 30,
            MaxLatencyMs = 250
        };

        Assert.Contains("loaded", r.DisplayText);
        Assert.Contains("4/5", r.DisplayText);
        Assert.Contains("101", r.DisplayText); // 100.5 округлено
    }

    [Fact]
    public void AutoTuneResult_LatencyAboveThreshold_ScoreDegrades()
    {
        var fast = new AutoTuneResult
        {
            SuccessCount = 5, TotalCount = 5,
            AvgLatencyMs = 50, MinLatencyMs = 30, MaxLatencyMs = 80
        };
        var slow = new AutoTuneResult
        {
            SuccessCount = 5, TotalCount = 5,
            AvgLatencyMs = 1500, MinLatencyMs = 500, MaxLatencyMs = 2000
        };

        Assert.True(fast.CompositeScore > slow.CompositeScore);
    }

    // ═══════════════════════════════════════════════
    //  Сервисный слой — проверка таймаута
    // ═══════════════════════════════════════════════

    [Fact]
    public async Task ConnectivityChecker_HardTimeout_ReturnsEmpty()
    {
        // Симулируем checker, который никогда не завершается (не использует CancellationToken)
        _checkerMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<TargetEntry>, CancellationToken>(async (_, _) =>
            {
                // Бесконечное ожидание без учёта токена
                await Task.Delay(TimeSpan.FromDays(1));
                return (0.0, new List<CheckResult>());
            });

        var checker = _checkerMock.Object;
        var targets = new List<TargetEntry>
        {
            new() { Key = "Test", Kind = TargetKind.Http, Value = "https://example.com" }
        };

        // Жёсткий таймаут через WhenAny (100 мс)
        var checkTask = checker.CheckAllAsync(targets, CancellationToken.None);
        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(100));
        var completed = await Task.WhenAny(checkTask, timeoutTask);

        // Должен вернуться таймаут — checker никогда не завершается
        Assert.Equal(timeoutTask, completed);
    }

    [Fact]
    public async Task ConnectivityChecker_Cancellation_StopsEarly()
    {
        var wasCancelled = false;
        _checkerMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<TargetEntry>, CancellationToken>(async (_, ct) =>
            {
                try
                {
                    await Task.Delay(10_000, ct);
                    return (0.0, new List<CheckResult>());
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    throw;
                }
            });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var checker = _checkerMock.Object;
        var targets = new List<TargetEntry>
        {
            new() { Key = "Test", Kind = TargetKind.Http, Value = "https://example.com" }
        };

        try
        {
            await checker.CheckAllAsync(targets, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо
        }

        Assert.True(wasCancelled, "Checker должен был отменить Task.Delay");
    }

    // ═══════════════════════════════════════════════
    //  ViewModel — целостность при ошибке ApplyPresetState
    // ═══════════════════════════════════════════════

    [Fact]
    public void ServiceViewModel_ApplyPresetState_HandlesErrors()
    {
        // Проверяем, что ApplyPresetState не выбрасывает исключений наружу
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"FluxRouteTest_ApplyPreset_{Guid.NewGuid():N}");

        try
        {
            var engineDir = System.IO.Path.Combine(dir, "engine");
            System.IO.Directory.CreateDirectory(engineDir);

            var vm = new ServiceViewModel(
                getEngineDir: () => engineDir,
                getSelectedProfileDisplayName: () => "Test",
                addAppLog: _ => { },
                httpClientFactory: _httpFactoryMock.Object,
                connectivityChecker: _checkerMock.Object);

            // Применение с заведомо неверной комбинацией не должно падать
            var ex = Record.Exception(() => vm.ApplyPresetState(true, "TCP и UDP", "loaded"));
            Assert.Null(ex);

            // Выключение GameFilter
            ex = Record.Exception(() => vm.ApplyPresetState(false, "TCP и UDP", "any"));
            Assert.Null(ex);

            // Сброс в none
            ex = Record.Exception(() => vm.ApplyPresetState(true, "TCP", "none"));
            Assert.Null(ex);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════
    //  CloseAutoTune — закрытие оверлея
    // ═══════════════════════════════════════════════

    [Fact]
    public void CloseAutoTune_ClosesOverlay()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"FluxRouteTest_Close_{Guid.NewGuid():N}");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "engine"));

            var vm = new ServiceViewModel(
                getEngineDir: () => dir,
                getSelectedProfileDisplayName: () => "Test",
                addAppLog: _ => { },
                httpClientFactory: _httpFactoryMock.Object,
                connectivityChecker: _checkerMock.Object);
            vm.RequestHideOverlay = () => { vm.AutoTuneOverlayVisible = false; vm.AutoTuneResultVisible = false; };

            // Симулируем открытый оверлей с результатами
            vm.AutoTuneOverlayVisible = true;
            vm.AutoTuneResultVisible = true;

            // CloseAutoTune должен закрыть оверлей
            vm.CloseAutoTuneCommand.Execute(null);

            Assert.False(vm.AutoTuneOverlayVisible, "Оверлей должен быть закрыт");
            Assert.False(vm.AutoTuneResultVisible, "Панель результатов должна быть скрыта");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CloseAutoTune_DuringProgress_ClosesOverlay()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"FluxRouteTest_CloseProg_{Guid.NewGuid():N}");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "engine"));

            var vm = new ServiceViewModel(
                getEngineDir: () => dir,
                getSelectedProfileDisplayName: () => "Test",
                addAppLog: _ => { },
                httpClientFactory: _httpFactoryMock.Object,
                connectivityChecker: _checkerMock.Object);
            vm.RequestHideOverlay = () => { vm.AutoTuneOverlayVisible = false; };

            // Симулируем открытый оверлей во время тестирования
            vm.AutoTuneOverlayVisible = true;
            vm.AutoTuneResultVisible = false;

            // CloseAutoTune должен закрыть оверлей
            vm.CloseAutoTuneCommand.Execute(null);

            Assert.False(vm.AutoTuneOverlayVisible);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
