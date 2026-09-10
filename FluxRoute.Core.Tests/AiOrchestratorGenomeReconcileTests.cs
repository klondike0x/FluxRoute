using System.IO;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты согласования отслеживаемого генотипа ИИ с рабочим профилем (issue #89, релиз v1.7.1).
/// Полный скан останавливает последний проверенный профиль, но оставляет его выбранным, а затем
/// запускает лучшую стратегию. Если после такого переключения оставить прежний _currentGenome,
/// следующий цикл проверит НОВЫЙ профиль, а результат запишет в историю/бандит под чужим Id
/// (правка по Codex P1, ревью #97).
/// </summary>
public sealed class AiOrchestratorGenomeReconcileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _engineDir;
    private readonly AiStrategyRegistry _registry;
    private readonly AiOrchestratorService _service;
    private ProfileItem? _activeProfile;

    public AiOrchestratorGenomeReconcileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteReconcileTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var engineDir = Path.Combine(_tempDir, "engine");
        Directory.CreateDirectory(engineDir);
        _engineDir = engineDir;

        _registry = new AiStrategyRegistry(Path.Combine(_tempDir, "registry.json"));
        var history = new AiHistoryStore(Path.Combine(_tempDir, "fluxroute-ai-history.jsonl"));

        var connectivityMock = new Mock<IConnectivityChecker>();
        connectivityMock
            .Setup(c => c.CheckAllAsync(
                It.IsAny<IEnumerable<TargetEntry>>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1.0, new List<CheckResult>()));

        var fingerprints = new NetworkFingerprintProvider();
        var materializer = new BatMaterializer();
        var bandit = new BanditSelector(_registry, new Random(12345));
        var evolver = new StrategyEvolver(
            _registry,
            history,
            materializer,
            () => engineDir,
            () => new AiSettings());

        _service = new AiOrchestratorService(
            getProfiles: () => new List<ProfileItem>(),
            getActiveProfile: () => _activeProfile,
            switchProfile: _ => Task.CompletedTask,
            getTargetsPath: () => "",
            notifyScoreUpdate: (_, _) => Task.CompletedTask,
            engineDir: () => engineDir,
            () => new AiSettings(),
            refreshProfiles: () => Task.CompletedTask,
            isWinwsRunning: () => false,
            ensureProtectionRunning: () => Task.CompletedTask,
            connectivityMock.Object,
            fingerprints,
            new NetworkChangeWatcher(fingerprints),
            _registry,
            history,
            bandit,
            evolver,
            materializer);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static ProfileItem Profile(string fileName, string displayName, string fullPath) =>
        new() { FileName = fileName, DisplayName = displayName, FullPath = fullPath };

    private static StrategyGenome Genome(string? batFileName, string displayName, string? sourceBatPath,
        StrategyOrigin origin = StrategyOrigin.Builtin) =>
        new()
        {
            BatFileName = batFileName,
            DisplayName = displayName,
            SourceBatPath = sourceBatPath,
            Origin = origin,
            OrchestratorEnabled = true,
        };

    [Fact]
    public void ReconcileGenomeWithActiveProfile_RefreshesStaleBatPath_WhenPortableInstallMoved()
    {
        var evolvedDir = Path.Combine(_engineDir, "ai-evolved");
        Directory.CreateDirectory(evolvedDir);
        var evolvedBat = Path.Combine(evolvedDir, "evolved_v1.bat");
        File.WriteAllText(evolvedBat, "@echo off");

        // Установка переехала: в реестре остался прежний абсолютный путь к тому же BAT.
        var genome = Genome("evolved_v1.bat", "evolved_v1",
            @"C:\СтароеМесто\engine\ai-evolved\evolved_v1.bat", StrategyOrigin.Evolved);
        _service.CurrentGenomeForTests = genome;
        _activeProfile = Profile("evolved_v1.bat", "evolved_v1", evolvedBat);

        var reset = _service.ReconcileGenomeWithActiveProfile();

        // Это своя же стратегия — генотип сохраняется, а устаревший путь подтягивается из ai-evolved.
        Assert.False(reset);
        Assert.Same(genome, _service.CurrentGenomeForTests);
        Assert.Equal(evolvedBat, genome.SourceBatPath);
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_DoesNotAdoptEvolvedBat_ForBuiltinGenome()
    {
        var evolvedDir = Path.Combine(_engineDir, "ai-evolved");
        Directory.CreateDirectory(evolvedDir);
        var evolvedBat = Path.Combine(evolvedDir, "general.bat");
        File.WriteAllText(evolvedBat, "@echo off");

        // Одноимённый evolved-BAT не «усыновляется» встроенным генотипом: у встроенных канонический
        // путь — engine\<имя>, и заново регистрирует их SyncBuiltins.
        _service.CurrentGenomeForTests = Genome("general.bat", "general",
            @"C:\СтароеМесто\engine\general.bat");
        _activeProfile = Profile("general.bat", "general", evolvedBat);

        Assert.True(_service.ReconcileGenomeWithActiveProfile());
        Assert.Null(_service.CurrentGenomeForTests);
    }

    [Fact]
    public void GenomeMatchesProfile_MatchesByBatFile_DisplayName_OrSourcePath()
    {
        var profile = Profile("general.bat", "general", @"C:\engine\general.bat");

        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome("general.bat", "другое имя", null), profile));
        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome(null, "GENERAL", null), profile));
        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome(null, "другое имя", @"c:\ENGINE\GENERAL.BAT"), profile));
    }

    [Fact]
    public void GenomeMatchesProfile_DoesNotMatchDifferentProfile()
    {
        var profile = Profile("general.bat", "general", @"C:\engine\general.bat");

        Assert.False(AiOrchestratorService.GenomeMatchesProfile(
            Genome("evolved_v1.bat", "evolved_v1", @"C:\engine\ai-evolved\evolved_v1.bat"), profile));
    }

    [Fact]
    public void GenomeMatchesProfile_IgnoresEmptyValues()
    {
        // Профиль без имени и пути не должен «подходить» генотипу без имени и пути.
        var profile = Profile("", "", "");

        Assert.False(AiOrchestratorService.GenomeMatchesProfile(Genome(null, "", null), profile));
        Assert.False(AiOrchestratorService.GenomeMatchesProfile(Genome("   ", "  ", "  "), profile));
    }

    [Fact]
    public void GenomeMatchesProfile_DistinguishesSameNameBuiltinAndEvolvedBat()
    {
        // LoadProfiles при совпадении имён отдаёт предпочтение ai-evolved-пути: одноимённые BAT —
        // РАЗНЫЕ стратегии, поэтому чужой генотип нельзя принять по имени (Codex P1, ревью #97).
        var evolvedProfile = Profile("general.bat", "general", @"C:\engine\ai-evolved\general.bat");

        Assert.False(AiOrchestratorService.GenomeMatchesProfile(
            Genome("general.bat", "general", @"C:\engine\general.bat"), evolvedProfile));
    }

    [Fact]
    public void GenomeMatchesProfile_FallsBackToName_WhenPathIsKnownOnOneSideOnly()
    {
        var profile = Profile("general.bat", "general", @"C:\engine\ai-evolved\general.bat");

        // У генотипа путь не записан (исходный BAT неизвестен) — решает имя файла.
        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome("general.bat", "general", null), profile));
    }

    [Fact]
    public void GenomeMatchesProfile_NormalizesSeparatorsAndCase_InPathComparison()
    {
        var profile = Profile("evolved_v1.bat", "evolved_v1", @"C:\engine\ai-evolved\evolved_v1.bat");

        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome("evolved_v1.bat", "evolved_v1", @"C:/ENGINE/AI-EVOLVED/EVOLVED_V1.BAT"), profile));
        Assert.True(AiOrchestratorService.GenomeMatchesProfile(
            Genome("evolved_v1.bat", "evolved_v1", @"   C:\engine\ai-evolved\evolved_v1.bat   "), profile));
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_ResetsGenome_WhenSameNameEvolvedPathIsActive()
    {
        _service.CurrentGenomeForTests = Genome("general.bat", "general", @"C:\engine\general.bat");
        _service.ConsecutiveFailuresForTests = 1;
        // Скан запустил ai-evolved BAT с тем же именем: генотип встроенной стратегии уже не подходит,
        // иначе результат проверившегося профиля записался бы под Id встроенного генотипа.
        _activeProfile = Profile("general.bat", "general", @"C:\engine\ai-evolved\general.bat");

        var reset = _service.ReconcileGenomeWithActiveProfile();

        Assert.True(reset);
        Assert.Null(_service.CurrentGenomeForTests);
        Assert.Equal(0, _service.ConsecutiveFailuresForTests);
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_KeepsGenome_WhenActiveProfileMatches()
    {
        var genome = Genome("general.bat", "general", @"C:\engine\general.bat");
        _service.CurrentGenomeForTests = genome;
        _service.ConsecutiveFailuresForTests = 1;
        _activeProfile = Profile("general.bat", "general", @"C:\engine\general.bat");

        var reset = _service.ReconcileGenomeWithActiveProfile();

        Assert.False(reset);
        Assert.Same(genome, _service.CurrentGenomeForTests);
        // Свой профиль остался — серия неудач по нему сохраняется.
        Assert.Equal(1, _service.ConsecutiveFailuresForTests);
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_ResetsGenome_WhenBestProfileIsAnotherStrategy()
    {
        var genome = Genome("general.bat", "general", @"C:\engine\general.bat");
        _service.CurrentGenomeForTests = genome;
        _service.ConsecutiveFailuresForTests = 2;
        // Скан выбрал и запустил другую стратегию — отслеживаемый генотип больше не соответствует
        // рабочему профилю и должен быть сброшен, иначе результат уйдёт под старым Id.
        _activeProfile = Profile("evolved_v1.bat", "evolved_v1", @"C:\engine\ai-evolved\evolved_v1.bat");

        var reset = _service.ReconcileGenomeWithActiveProfile();

        Assert.True(reset);
        Assert.Null(_service.CurrentGenomeForTests);
        // Серия неудач относилась к прежнему генотипу: новая стратегия не должна быть снята
        // после первой же осечки из-за унаследованного счётчика (Codex P2, ревью #97).
        Assert.Equal(0, _service.ConsecutiveFailuresForTests);
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_ResetsGenome_WhenNoProfileIsActive()
    {
        _service.CurrentGenomeForTests = Genome("general.bat", "general", @"C:\engine\general.bat");
        _activeProfile = null;

        var reset = _service.ReconcileGenomeWithActiveProfile();

        Assert.True(reset);
        Assert.Null(_service.CurrentGenomeForTests);
    }

    [Fact]
    public void ReconcileGenomeWithActiveProfile_ReturnsFalse_WhenNothingIsTracked()
    {
        _service.CurrentGenomeForTests = null;
        _activeProfile = Profile("general.bat", "general", @"C:\engine\general.bat");

        Assert.False(_service.ReconcileGenomeWithActiveProfile());
        Assert.Null(_service.CurrentGenomeForTests);
    }
}
