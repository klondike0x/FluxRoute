using System.IO;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для отдельного действия очистки слабых эволюций (issue #89):
/// раньше слабые эволюции удалялись тайно внутри «Проверить сейчас»,
/// теперь — только по явной кнопке «Очистить слабые эволюции».
/// Правка по Codex: очистка учитывает сеть — кандидаты отбираются по последнему
/// результату именно на ТЕКУЩЕЙ сети, а не по глобальному LastVerificationScore.
/// </summary>
public sealed class AiOrchestratorPurgeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly AiOrchestratorService _service;
    private readonly NetworkFingerprintProvider _fingerprints;
    private const int Threshold = 60;

    public AiOrchestratorPurgeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRoutePurgeTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var engineDir = Path.Combine(_tempDir, "engine");
        Directory.CreateDirectory(engineDir);

        _registry = new AiStrategyRegistry(Path.Combine(_tempDir, "registry.json"));
        _history = new AiHistoryStore(Path.Combine(_tempDir, "fluxroute-ai-history.jsonl"));

        var connectivityMock = new Mock<IConnectivityChecker>();
        connectivityMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1.0, new List<CheckResult>()));

        _fingerprints = new NetworkFingerprintProvider();
        var materializer = new BatMaterializer();
        var bandit = new BanditSelector(_registry, new Random(12345));
        var evolver = new StrategyEvolver(
            _registry,
            _history,
            materializer,
            () => engineDir,
            () => new AiSettings { AutoDeleteBelowScore = Threshold });
        var watcher = new NetworkChangeWatcher(_fingerprints);
        var settings = () => new AiSettings { AutoDeleteBelowScore = Threshold };

        _service = new AiOrchestratorService(
            getProfiles: () => new List<ProfileItem>(),
            getActiveProfile: () => null,
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => "",
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            engineDir: () => engineDir,
            settings,
            refreshProfiles: () => Task.CompletedTask,
            isWinwsRunning: () => false,
            ensureProtectionRunning: () => Task.CompletedTask,
            connectivityMock.Object,
            _fingerprints,
            watcher,
            _registry,
            _history,
            bandit,
            evolver,
            materializer);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private StrategyGenome AddEvolved(string name, int score) =>
        AddGenome(name, StrategyOrigin.Evolved, score);

    private StrategyGenome AddBuiltin(string name, int score) =>
        AddGenome(name, StrategyOrigin.Builtin, score);

    private StrategyGenome AddGenome(string name, StrategyOrigin strategyOrigin, int score)
    {
        var g = new StrategyGenome
        {
            DisplayName = name,
            Origin = strategyOrigin,
            OrchestratorEnabled = true,
            LastVerificationScore = score,
            LastVerifiedAt = DateTimeOffset.UtcNow,
        };
        _registry.Upsert(g);
        _registry.Save();
        return g;
    }

    // Пишет outcome проверки генотипа на указанной сети.
    private void SeedOutcome(Guid genomeId, string networkHash, int score, DateTimeOffset? timestamp = null)
        => _history.Append(new ProbeOutcome
        {
            GenomeId = genomeId,
            NetworkHash = networkHash,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Score = score,
            ProcessStable = true,
        });

    [Fact]
    public async Task PurgeWeakEvolutions_DeletesWeakEvolution_WhenBuiltinOk()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();

        // Проходящая встроенная стратегия + слабая эволюция НА ЭТОЙ СЕТИ
        SeedOutcome(builtin.Id, fp.Hash, 90);
        SeedOutcome(weak.Id, fp.Hash, 30);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(1, deleted);
        Assert.Null(_registry.GetById(weak.Id));
        Assert.NotNull(_registry.GetById(builtin.Id));
    }

    [Fact]
    public async Task PurgeWeakEvolutions_SkipsEvolutionGoodOnThisNetwork_ReturnsZero()
    {
        var builtin = AddBuiltin("general", 90);
        var strong = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();

        // Эволюция "слаба" глобально (LastVerificationScore=30), но НА ЭТОЙ СЕТИ прошла — не кандидат.
        SeedOutcome(builtin.Id, fp.Hash, 90);
        SeedOutcome(strong.Id, fp.Hash, 90);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(0, deleted);
        Assert.NotNull(_registry.GetById(strong.Id));
    }

    [Fact]
    public async Task PurgeWeakEvolutions_KeepsWeakEvolution_WhenNoBuiltinPasses()
    {
        var builtinLow = AddBuiltin("general2", 30);
        var weak = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();

        // Слабая эволюция есть, но проходящих встроенных нет → guard #62, удалять несправедливо.
        SeedOutcome(builtinLow.Id, fp.Hash, 30);
        SeedOutcome(weak.Id, fp.Hash, 30);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(0, deleted);
        Assert.NotNull(_registry.GetById(weak.Id));
    }

    [Fact]
    public async Task PurgeWeakEvolutions_BuiltinPassedBeforeButFailsNow_KeepsEvolution()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();

        // Встроенная стратегия: раньше проходила (90), но её ПОСЛЕДНИЙ результат на этой сети — 30.
        // Устаревший успех не должен позволять удалять эволюции (правка по Codex P1).
        SeedOutcome(builtin.Id, fp.Hash, 90, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));
        SeedOutcome(builtin.Id, fp.Hash, 30, timestamp: DateTimeOffset.UtcNow);
        SeedOutcome(weak.Id, fp.Hash, 30);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(0, deleted);
        Assert.NotNull(_registry.GetById(weak.Id));
    }

    [Fact]
    public async Task PersistScanVerification_RecordsOutcome_SoPurgeFindsCandidate()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);

        // «Сканировать все стратегии» переносит результаты в историю/геном через PersistScanVerification
        // (gated-обёртка). Вызов обязательно дожидаемся: импорт асинхронный, иначе purge успел бы
        // отработать до записи наблюдений и тест стал бы флаки.
        await _service.PersistScanVerification(
            new[]
            {
                (builtin.Id, new ProfileProbeResult { Score = 90, SuccessRate = 0.9, ProcessStable = true }),
                (weak.Id, new ProfileProbeResult { Score = 30, SuccessRate = 0.3, ProcessStable = true }),
            },
            _fingerprints.Capture().Hash);

        // После этого очистка должна найти кандидата и удалить его (защита #62 соблюдена: builtin ок).
        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(1, deleted);
        Assert.Null(_registry.GetById(weak.Id));
    }

    [Fact]
    public async Task ApplyScanResults_UnderSerializedScanGate_RecordsOutcome_SoPurgeFindsCandidate()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);

        // Production-путь «Сканировать все стратегии»: полный скан и импорт его результатов идут под
        // ОДНИМ удержанием мьютекса (RunSerializedAsync), импорт — через негейтедное ядро
        // ApplyScanResults (релизный PR #76, P2). Проверяем именно эту связку.
        await _service.RunSerializedAsync(async () =>
        {
            _service.ApplyScanResults(
                new[]
                {
                    (builtin.Id, new ProfileProbeResult { Score = 90, SuccessRate = 0.9, ProcessStable = true }),
                    (weak.Id, new ProfileProbeResult { Score = 30, SuccessRate = 0.3, ProcessStable = true }),
                },
                _fingerprints.Capture().Hash);

            await Task.CompletedTask.ConfigureAwait(false);
        });

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(1, deleted);
        Assert.Null(_registry.GetById(weak.Id));
    }

    [Fact]
    public async Task PurgeCore_UnderSerializedGate_DeletesWithoutDeadlock()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();
        SeedOutcome(builtin.Id, fp.Hash, 90);
        SeedOutcome(weak.Id, fp.Hash, 30);

        // Production-путь кнопки очистки: снимок активного профиля, очистка и решение activeDeleted
        // идут под ОДНИМ удержанием мьютекса (RunSerializedAsync), а сама очистка — ядром без захвата.
        // Повторный WaitAsync по тому же SemaphoreSlim (он не реентерабелен) повесил бы кнопку навсегда,
        // поэтому проверяем не только результат, но и то, что операция завершается (Codex P1, ревью #76).
        var deleted = -1;
        var serialized = _service.RunSerializedAsync(async () =>
        {
            deleted = await _service.PurgeWeakEvolutionsCoreAsync().ConfigureAwait(false);
        });

        var finished = await Task.WhenAny(serialized, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(serialized, finished);
        await serialized;

        Assert.Equal(1, deleted);
        Assert.Null(_registry.GetById(weak.Id));
    }

    [Fact]
    public async Task PurgeWeakEvolutions_WaitsForGateHeldBySerializedRun()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);
        var fp = _fingerprints.Capture();
        SeedOutcome(builtin.Id, fp.Hash, 90);
        SeedOutcome(weak.Id, fp.Hash, 30);

        // Публичная (гейтедная) очистка обязана ждать чужое удержание гейта, а не выполнять удаление
        // параллельно с циклом/сканом: именно это гарантирует, что цикл не переключит профиль
        // в середине операции (Codex P1, ревью #76).
        var gateHeld = new TaskCompletionSource();
        var serialized = _service.RunSerializedAsync(async () => await gateHeld.Task.ConfigureAwait(false));

        var purge = _service.PurgeWeakEvolutionsAsync();
        await Task.Delay(300);
        Assert.False(purge.IsCompleted);

        gateHeld.SetResult();
        await serialized;
        Assert.Equal(1, await purge);
        Assert.Null(_registry.GetById(weak.Id));
    }
}
