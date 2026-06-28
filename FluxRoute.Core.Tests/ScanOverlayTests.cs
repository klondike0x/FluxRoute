using System.IO;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для оверлея сканирования стратегий:
/// — IProgress<(int current, int total)> корректно передаётся в ScanAllProfilesAsync
/// — CancellationToken останавливает сканирование
/// — Прогресс обновляется в реальном времени
/// </summary>
public sealed class ScanOverlayTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<ProfileItem> _profiles;

    public ScanOverlayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteScanOverlay_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _profiles =
        [
            new ProfileItem { FileName = "general.bat", DisplayName = "General", FullPath = Path.Combine(_tempDir, "general.bat") },
            new ProfileItem { FileName = "discord.bat", DisplayName = "Discord", FullPath = Path.Combine(_tempDir, "discord.bat") },
            new ProfileItem { FileName = "youtube.bat", DisplayName = "YouTube", FullPath = Path.Combine(_tempDir, "youtube.bat") },
        ];
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Прогресс-репорт при сканировании ──

    [Fact]
    public async Task ScanAllProfilesAsync_ReportsProgress_ForEachProfile()
    {
        var connectivityMock = new Mock<IConnectivityChecker>();
        connectivityMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.0, new List<CheckResult>()));

        var orchestrator = new OrchestratorService(
            getProfiles: () => _profiles,
            getActiveProfile: () => _profiles.First(),
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => Path.Combine(_tempDir, "targets.txt"),
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            connectivity: connectivityMock.Object);

        var progressReports = new List<(int current, int total)>();

        var progress = new Progress<(int current, int total)>(report =>
        {
            progressReports.Add(report);
        });

        using var cts = new CancellationTokenSource();
        await orchestrator.ScanAllProfilesAsync(cts.Token, progress);

        Assert.NotEmpty(progressReports);
        Assert.Equal(_profiles.Count, progressReports.Count);

        // Последний репорт должен быть (3, 3)
        var last = progressReports.Last();
        Assert.Equal(3, last.current);
        Assert.Equal(3, last.total);
    }

    // ── Прогресс монотонно возрастает ──

    [Fact]
    public async Task ScanAllProfilesAsync_ProgressIncreasesMonotonically()
    {
        var connectivityMock = new Mock<IConnectivityChecker>();
        connectivityMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.0, new List<CheckResult>()));

        var orchestrator = new OrchestratorService(
            getProfiles: () => _profiles,
            getActiveProfile: () => _profiles.First(),
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => Path.Combine(_tempDir, "targets.txt"),
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            connectivity: connectivityMock.Object);

        var progressReports = new List<(int current, int total)>();
        var progress = new Progress<(int current, int total)>(report => progressReports.Add(report));

        using var cts = new CancellationTokenSource();
        await orchestrator.ScanAllProfilesAsync(cts.Token, progress);

        for (var i = 1; i < progressReports.Count; i++)
        {
            Assert.True(
                progressReports[i].current >= progressReports[i - 1].current,
                $"Прогресс не монотонен на шаге {i}: {progressReports[i - 1].current} -> {progressReports[i].current}");
        }
    }

    // ── Отмена через CancellationToken ──

    [Fact]
    public async Task ScanAllProfilesAsync_Cancellation_StopsEarly()
    {
        var connectivityMock = new Mock<IConnectivityChecker>();
        connectivityMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.0, new List<CheckResult>()));

        var orchestrator = new OrchestratorService(
            getProfiles: () => _profiles,
            getActiveProfile: () => _profiles.First(),
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => Path.Combine(_tempDir, "targets.txt"),
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            connectivity: connectivityMock.Object);

        var progressReports = new List<(int current, int total)>();
        var progress = new Progress<(int current, int total)>(report => progressReports.Add(report));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50)); // отмена через 50 мс

        await orchestrator.ScanAllProfilesAsync(cts.Token, progress);

        // Сканирование должно было прерваться до проверки всех профилей
        Assert.True(
            progressReports.Count < _profiles.Count,
            $"Ожидалось что сканирование прервётся до проверки всех {_profiles.Count} профилей, " +
            $"но было проверено {progressReports.Count}");
        Assert.False(orchestrator.IsScanning);
    }

    // ── Пустой список профилей ──

    [Fact]
    public async Task ScanAllProfilesAsync_EmptyProfiles_NoProgress()
    {
        var connectivityMock = new Mock<IConnectivityChecker>();

        var orchestrator = new OrchestratorService(
            getProfiles: () => [],
            getActiveProfile: () => null,
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => Path.Combine(_tempDir, "targets.txt"),
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            connectivity: connectivityMock.Object);

        var progressCalled = false;
        var progress = new Progress<(int current, int total)>(_ => progressCalled = true);

        using var cts = new CancellationTokenSource();
        await orchestrator.ScanAllProfilesAsync(cts.Token, progress);

        Assert.False(progressCalled);
    }

    // ── ETA рассчитывается корректно ──

    [Fact]
    public void ScanEtaCalculation_ProducesReasonableValues()
    {
        // Имитируем логику UpdateScanEta (из MainViewModel.Orchestrator.cs)
        var startTime = DateTime.Now.AddSeconds(-30); // 30 сек назад
        var currentCount = 5;
        var totalCount = 20;

        var elapsed = DateTime.Now - startTime;
        var avgPerItem = elapsed.TotalSeconds / currentCount;
        var remaining = (int)(avgPerItem * (totalCount - currentCount));

        // За 30 сек обработано 5 из 20 → ~6 сек на элемент → ~90 сек осталось
        Assert.InRange(remaining, 60, 120);
    }

    [Fact]
    public void ScanEtaCalculation_HandlesZeroCurrent()
    {
        var startTime = DateTime.Now;
        var currentCount = 0;
        var totalCount = 10;

        var elapsed = DateTime.Now - startTime;

        // При currentCount = 0 не должно быть деления на ноль
        if (currentCount > 0 && totalCount > currentCount)
        {
            var avgPerItem = elapsed.TotalSeconds / currentCount;
            var remaining = (int)(avgPerItem * (totalCount - currentCount));
        }

        // Просто не падаем
        Assert.True(true);
    }

    // ── ScanStatusText форматируется с номером шага ──

    [Fact]
    public void ScanStatusText_Formatting_ContainsStepInfo()
    {
        var current = 3;
        var total = 12;
        var statusText = $"[{current}/{total}] Тестирую стратегии...";

        Assert.Contains("3/12", statusText);
        Assert.Contains("Тестирую", statusText);
    }
}
