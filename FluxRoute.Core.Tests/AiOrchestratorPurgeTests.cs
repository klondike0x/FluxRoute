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
/// </summary>
public sealed class AiOrchestratorPurgeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _historyPath;
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

        _historyPath = Path.Combine(_tempDir, "fluxroute-ai-history.jsonl");
        _registry = new AiStrategyRegistry(Path.Combine(_tempDir, "registry.json"));
        _history = new AiHistoryStore(_historyPath);

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
        AddGenome(name, strategyOrigin: StrategyOrigin.Evolved, score: score);

    private StrategyGenome AddBuiltin(string name, int score) =>
        AddGenome(name, strategyOrigin: StrategyOrigin.Builtin, score: score);

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

    // Регистрирует в истории проходящую встроенную стратегию на текущей сети (условие #62).
    private void SeedBuiltinOk(string networkHash, Guid builtinId)
    {
        _history.Append(new ProbeOutcome
        {
            GenomeId = builtinId,
            NetworkHash = networkHash,
            Score = 90,
            ProcessStable = true,
        });
    }

    [Fact]
    public async Task PurgeWeakEvolutions_DeletesWeakEvolution_WhenBuiltinOk()
    {
        var builtin = AddBuiltin("general", 90);
        var weak = AddEvolved("evolved_v1", 30);

        var fp = _fingerprints.Capture();
        SeedBuiltinOk(fp.Hash, builtin.Id);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(1, deleted);
        Assert.Null(_registry.GetById(weak.Id));
        Assert.NotNull(_registry.GetById(builtin.Id));
    }

    [Fact]
    public async Task PurgeWeakEvolutions_NothingToPurge_ReturnsZero()
    {
        AddBuiltin("general", 90);

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task PurgeWeakEvolutions_KeepsWeakEvolution_WhenNoBuiltinPasses()
    {
        // Другой builtin, но с результатом НИЖЕ порога — сеть агрессивна (guard #62).
        var builtinLow = AddBuiltin("general2", 30);
        var weak = AddEvolved("evolved_v1", 30);

        var fp = _fingerprints.Capture();
        // Ни одно проходящее встроенное событие на этой сети нет → удалять несправедливо.
        _history.Append(new ProbeOutcome
        {
            GenomeId = builtinLow.Id,
            NetworkHash = fp.Hash,
            Score = 30,
        });

        var deleted = await _service.PurgeWeakEvolutionsAsync();

        Assert.Equal(0, deleted);
        Assert.NotNull(_registry.GetById(weak.Id));
    }
}
