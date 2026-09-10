using System.IO;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Целостная проверка выбора стратегии после полного сканирования (issue #84, релиз v1.7.1).
/// Скан измеряет все стратегии, но сам цикл ИИ стратегию не меняет: он переключается только по серии
/// неудач или при смене сети, поэтому ИИ мог «сидеть» на 76%, когда в списке уже была 85%. Закрывается
/// это связкой со стороны ВМ: запустить лучший по скану профиль и согласовать состояние ИИ
/// (<c>MainViewModel.Orchestrator</c>: BestRankedProfile → запуск → ReconcileGenomeWithActiveProfile).
/// Здесь связка прогоняется целиком на мок-движке (BAT-заглушки в engine + поддельная связность),
/// а проверяется главное: дальше ИИ работает на сильной стратегии, и результат очередной проверки
/// записывается под ЕЁ генотипом, а не под прежней слабой (симптом «46 проверок на ALT, по 1 на остальных»).
/// </summary>
public sealed class AiOrchestratorScanSelectionTests : IDisposable
{
    private const int WeakScore = 76;    // «general (ALT)» из отчёта
    private const int StrongScore = 85;  // «general11 (ALT)»
    private const int WeakProbes = 46;   // 35 успехов / 11 сбоев
    private const int StrongProbes = 41; // 35 успехов / 6 сбоев

    private readonly string _tempDir;
    private readonly string _engineDir;
    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly BanditSelector _bandit;
    private readonly AiOrchestratorService _service;
    private readonly NetworkFingerprintProvider _fingerprints;

    private readonly StrategyGenome _weak;
    private readonly StrategyGenome _strong;
    private readonly ProfileItem _weakProfile;
    private readonly ProfileItem _strongProfile;
    private readonly List<ProfileItem?> _switches = [];
    private ProfileItem? _activeProfile;

    public AiOrchestratorScanSelectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteScanSelectionTests_{Guid.NewGuid():N}");
        _engineDir = Path.Combine(_tempDir, "engine");
        Directory.CreateDirectory(_engineDir);

        // BAT-заглушки: ResolveProfile принимает только реально существующий файл, поэтому все
        // переключения в тесте идут по настоящим путям, как в engine пользователя.
        var weakBat = Path.Combine(_engineDir, "general-ALT.bat");
        var strongBat = Path.Combine(_engineDir, "general11-ALT.bat");
        File.WriteAllText(weakBat, "@echo off");
        File.WriteAllText(strongBat, "@echo off");

        _weakProfile = Profile("general-ALT.bat", "general (ALT)", weakBat);
        _strongProfile = Profile("general11-ALT.bat", "general11 (ALT)", strongBat);
        _weak = Genome("general-ALT.bat", "general (ALT)", weakBat);
        _strong = Genome("general11-ALT.bat", "general11 (ALT)", strongBat);

        _registry = new AiStrategyRegistry(Path.Combine(_tempDir, "registry.json"));
        _registry.Upsert(_weak);
        _registry.Upsert(_strong);
        _registry.Save();

        _history = new AiHistoryStore(Path.Combine(_tempDir, "fluxroute-ai-history.jsonl"));

        // Мок движка: цели всегда «отвечают». Итоговый счёт всё равно зависит от запущенного winws
        // (ProfileProbeOptions.RequireWinwsProcess), поэтому утверждения теста на счёт не опираются —
        // проверяется, ПОД ЧЬИМ генотипом записана проверка.
        var connectivity = new Mock<IConnectivityChecker>();
        connectivity
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<CheckResult>>()))
            .Returns((IEnumerable<TargetEntry> _, bool _, int _, CancellationToken _, IProgress<CheckResult>? _) =>
                Task.FromResult<(double, List<CheckResult>)>((1.0, HealthyChecks())));

        _fingerprints = new NetworkFingerprintProvider();
        var materializer = new BatMaterializer();
        _bandit = new BanditSelector(_registry, new Random(12345));
        var evolver = new StrategyEvolver(
            _registry,
            _history,
            materializer,
            () => _engineDir,
            () => new AiSettings());

        _service = new AiOrchestratorService(
            getProfiles: () => new List<ProfileItem> { _weakProfile, _strongProfile },
            getActiveProfile: () => _activeProfile,
            switchProfile: p =>
            {
                _switches.Add(p);
                _activeProfile = p;
                return Task.CompletedTask;
            },
            getTargetsPath: () => "",
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            engineDir: () => _engineDir,
            // Эволюция в тесте не нужна: она материализует BAT и уводит проверку в другую ветку.
            aiSettings: () => new AiSettings { MinProbesBeforeEvolve = int.MaxValue },
            refreshProfiles: () => Task.CompletedTask,
            isWinwsRunning: () => false,
            ensureProtectionRunning: () => Task.CompletedTask,
            connectivity.Object,
            _fingerprints,
            new NetworkChangeWatcher(_fingerprints),
            _registry,
            _history,
            _bandit,
            evolver,
            materializer);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static ProfileItem Profile(string fileName, string displayName, string fullPath) =>
        new() { FileName = fileName, DisplayName = displayName, FullPath = fullPath };

    private static StrategyGenome Genome(string batFileName, string displayName, string sourceBatPath) =>
        new()
        {
            BatFileName = batFileName,
            DisplayName = displayName,
            SourceBatPath = sourceBatPath,
            Origin = StrategyOrigin.Builtin,
            OrchestratorEnabled = true,
        };

    private static List<CheckResult> HealthyChecks() =>
    [
        new CheckResult { Key = "youtube", Ok = true, ElapsedMs = 120 },
        new CheckResult { Key = "discord", Ok = true, ElapsedMs = 140 },
        new CheckResult { Key = "twitch", Ok = true, ElapsedMs = 160 },
    ];

    /// <summary>История сети из отчёта: слабая 76% за 46 проб, сильная 85% за 41 пробу.</summary>
    private void SeedNetworkHistory(string hash)
    {
        for (var i = 0; i < 35; i++)
            _registry.RecordBanditSuccess(_weak.Id, hash);
        for (var i = 0; i < WeakProbes - 35; i++)
            _registry.RecordBanditFailure(_weak.Id, hash);

        for (var i = 0; i < 35; i++)
            _registry.RecordBanditSuccess(_strong.Id, hash);
        for (var i = 0; i < StrongProbes - 35; i++)
            _registry.RecordBanditFailure(_strong.Id, hash);
    }

    /// <summary>Импорт результатов уже выполненного скана production-путём (гейтедная обёртка, ревью #97).</summary>
    private Task ImportScanResultsAsync(string hash) =>
        _service.PersistScanVerification(
            new (Guid, ProfileProbeResult)[]
            {
                (_weak.Id, new ProfileProbeResult { Score = WeakScore, SuccessRate = 0.76, ProcessStable = true }),
                (_strong.Id, new ProfileProbeResult { Score = StrongScore, SuccessRate = 0.85, ProcessStable = true }),
            },
            hash);

    /// <summary>
    /// Состояние после полного скана: результаты импортированы, ИИ вёл слабую стратегию, а ВМ запустила
    /// лучшую по рейтингу и согласовала отслеживаемый генотип (реальный порядок в MainViewModel.Orchestrator).
    /// </summary>
    private async Task<string> ArrangeAfterScanAsync()
    {
        var hash = _fingerprints.Capture().Hash;
        SeedNetworkHistory(hash);
        await ImportScanResultsAsync(hash);

        _service.CurrentGenomeForTests = _weak;
        _activeProfile = _weakProfile;

        // ВМ: лучший профиль по рейтингу → запуск → согласование состояния ИИ.
        _activeProfile = _strongProfile;
        Assert.True(_service.ReconcileGenomeWithActiveProfile());
        Assert.Null(_service.CurrentGenomeForTests);

        return hash;
    }

    [Fact]
    public async Task AfterFullScan_NextCycleKeepsStrongestStrategy_EvenThoughWeakHasMoreProbes()
    {
        await ArrangeAfterScanAsync();

        // Следующий цикл ИИ: слабая набрала больше проб (46 против 41), но её средняя ниже — ИИ обязан
        // остаться на сильной, а не вернуть слабую «по привычке».
        await _service.CheckNowAsync();

        Assert.Equal(_strong.Id, _service.CurrentGenomeForTests!.Id);
        Assert.Equal(_strongProfile.FullPath, _activeProfile!.FullPath);
        Assert.Equal(_strongProfile.FullPath, _switches[^1]!.FullPath);
    }

    [Fact]
    public async Task AfterFullScan_ProbeOutcomeGoesToStrongGenome_NotToThePreviousWeakOne()
    {
        var hash = await ArrangeAfterScanAsync();
        var weakPulls = _registry.SumPullsForGenomeOnNetwork(_weak.Id, hash);
        var strongPulls = _registry.SumPullsForGenomeOnNetwork(_strong.Id, hash);

        await _service.CheckNowAsync(); // подбор и запуск сильной
        await _service.CheckNowAsync(); // проверка сильной → запись результата

        var outcomes = _history.LoadForNetwork(hash);
        Assert.Equal(_strong.Id, outcomes[^1].GenomeId);
        Assert.Equal(strongPulls + 1, _registry.SumPullsForGenomeOnNetwork(_strong.Id, hash));
        Assert.Equal(weakPulls, _registry.SumPullsForGenomeOnNetwork(_weak.Id, hash));
    }

    [Fact]
    public void BestKnownForNetwork_PrefersHigherScore_WhenWeakerStrategyHasMoreProbes()
    {
        var hash = _fingerprints.Capture().Hash;
        SeedNetworkHistory(hash);

        var pick = _bandit.BestKnownForNetwork([_weak, _strong], hash);

        Assert.NotNull(pick);
        Assert.Equal(_strong.Id, pick!.Id);
    }
}
