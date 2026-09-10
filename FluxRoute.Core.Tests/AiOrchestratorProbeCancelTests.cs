using System.IO;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Отмена «Проверить сейчас» на выбранной стратегии (issue #89).
/// Stop во время проверки отменяет токен и гасит winws, но восстановление профиля в finally идёт
/// через SwitchProfileAsync, который всегда стартует защиту — то есть возврат профиля незаметно
/// отменял Stop. Поэтому при отменённом токене профиль не возвращаем (Codex P1, ревью PR #76).
/// </summary>
public sealed class AiOrchestratorProbeCancelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _engineDir;
    private readonly string _batPath;
    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly NetworkFingerprintProvider _fingerprints;
    private readonly ProfileItem _selectedProfile;
    private readonly List<ProfileItem?> _switches = [];
    private ProfileItem? _activeProfile;
    private Action<ProfileItem?>? _onSwitch;

    public AiOrchestratorProbeCancelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fluxroute-probe-" + Guid.NewGuid().ToString("N"));
        _engineDir = Path.Combine(_tempDir, "engine");
        Directory.CreateDirectory(_engineDir);
        _batPath = Path.Combine(_engineDir, "general-ALT.bat");
        File.WriteAllText(_batPath, "@echo off");

        _selectedProfile = new ProfileItem
        {
            FileName = "general-ALT.bat",
            DisplayName = "general (ALT)",
            FullPath = _batPath,
        };

        _registry = new AiStrategyRegistry(Path.Combine(_tempDir, "registry.json"));
        _registry.Upsert(new StrategyGenome
        {
            BatFileName = "general-ALT.bat",
            DisplayName = "general (ALT)",
            SourceBatPath = _batPath,
            Origin = StrategyOrigin.Builtin,
            OrchestratorEnabled = true,
        });
        _registry.Save();

        _history = new AiHistoryStore(Path.Combine(_tempDir, "history.jsonl"));
        _fingerprints = new NetworkFingerprintProvider();
        _activeProfile = _selectedProfile;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временная папка не критична для результата теста.
        }
    }

    /// <summary>
    /// Отмена во время пробы (кнопка Stop): профиль не возвращаем, повторного запуска защиты нет.
    /// </summary>
    [Fact]
    public async Task ProbeSelectedStrategy_WhenCancelled_DoesNotRestoreProfile()
    {
        using var cts = new CancellationTokenSource();
        // Stop приходит ровно тогда, когда проверка уже переключилась на пробный профиль.
        _onSwitch = profile =>
        {
            if (profile is not null)
                cts.Cancel();
        };
        var service = CreateService(new Mock<IConnectivityChecker>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProbeSelectedStrategyAsync(cts.Token));

        // Единственное переключение — на пробный профиль. Обратно на выбранный не ушли:
        // иначе SwitchProfileAsync снова поднял бы winws после Stop.
        var probeSwitch = Assert.Single(_switches);
        Assert.Equal(_batPath, probeSwitch!.FullPath);
        Assert.NotSame(_selectedProfile, _activeProfile);
    }

    /// <summary>
    /// Обратная сторона: если отмены не было, выбранный профиль обязан вернуться (иначе проверка
    /// оставляет пользователя на служебном профиле, которого нет в списке).
    /// </summary>
    [Fact]
    public async Task ProbeSelectedStrategy_WhenProbeFails_RestoresSelectedProfile()
    {
        var connectivity = new Mock<IConnectivityChecker>();
        connectivity
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<CheckResult>>()))
            .ThrowsAsync(new InvalidOperationException("движок недоступен"));
        var service = CreateService(connectivity);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProbeSelectedStrategyAsync(CancellationToken.None));

        Assert.Equal(2, _switches.Count);
        Assert.Same(_selectedProfile, _activeProfile);
    }

    private AiOrchestratorService CreateService(Mock<IConnectivityChecker> connectivity)
    {
        var materializer = new BatMaterializer();
        return new AiOrchestratorService(
            getProfiles: () => new List<ProfileItem> { _selectedProfile },
            getActiveProfile: () => _activeProfile,
            switchProfile: profile =>
            {
                _switches.Add(profile);
                _activeProfile = profile;
                _onSwitch?.Invoke(profile);
                return Task.CompletedTask;
            },
            getTargetsPath: () => "",
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            engineDir: () => _engineDir,
            aiSettings: () => new AiSettings { MinProbesBeforeEvolve = int.MaxValue },
            refreshProfiles: () => Task.CompletedTask,
            isWinwsRunning: () => false,
            ensureProtectionRunning: () => Task.CompletedTask,
            connectivity.Object,
            _fingerprints,
            new NetworkChangeWatcher(_fingerprints),
            _registry,
            _history,
            new BanditSelector(_registry, new Random(1)),
            new StrategyEvolver(_registry, _history, materializer, () => _engineDir, () => new AiSettings()),
            materializer);
    }
}
