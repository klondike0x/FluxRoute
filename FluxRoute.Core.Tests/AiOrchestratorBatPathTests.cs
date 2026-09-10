using System.IO;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты поиска BAT для генотипа (issue #89, релиз v1.7.1). Встроенная стратегия живёт в корне engine,
/// эволюционированная — в engine\ai-evolved. Раньше фолбэк по имени файла подставлял встроенному
/// генотипу одноимённый evolved-BAT, и путь эволюционированной стратегии закреплялся за встроенным
/// генотипом: согласование считало встроенный генотип владельцем живого профиля, а проверки и
/// результаты бандита записывались под чужим Id (правка по Codex P2, ревью #97).
/// </summary>
public sealed class AiOrchestratorBatPathTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _engineDir;
    private readonly string _evolvedDir;

    public AiOrchestratorBatPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteBatPathTests_{Guid.NewGuid():N}");
        _engineDir = Path.Combine(_tempDir, "engine");
        _evolvedDir = Path.Combine(_engineDir, "ai-evolved");
        Directory.CreateDirectory(_evolvedDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void FindExistingBatPath_PrefersStoredPath_WhenItExists()
    {
        var stored = CreateBat(_evolvedDir, "evolved_v1.bat");
        var genome = Genome("evolved_v1.bat", "evolved_v1", stored, StrategyOrigin.Evolved);

        Assert.Equal(stored, AiOrchestratorService.FindExistingBatPath(genome, _engineDir));
    }

    [Fact]
    public void FindExistingBatPath_DoesNotAdoptEvolvedBat_ForBuiltinGenome()
    {
        // Ключевой случай: у встроенного генотипа сохранённый путь устарел, а в ai-evolved лежит файл
        // с тем же именем. Это ДРУГАЯ стратегия — встроенному генотипу её путь не принадлежит.
        CreateBat(_evolvedDir, "general.bat");
        var genome = Genome("general.bat", "general", @"C:\СтароеМесто\engine\general.bat");

        Assert.Null(AiOrchestratorService.FindExistingBatPath(genome, _engineDir));
    }

    [Fact]
    public void FindExistingBatPath_UsesEngineRoot_ForBuiltinGenome()
    {
        var builtin = CreateBat(_engineDir, "general.bat");
        CreateBat(_evolvedDir, "general.bat");
        var genome = Genome("general.bat", "general", @"C:\СтароеМесто\engine\general.bat");

        Assert.Equal(builtin, AiOrchestratorService.FindExistingBatPath(genome, _engineDir));
    }

    [Fact]
    public void FindExistingBatPath_UsesEvolvedFolder_ForEvolvedGenome()
    {
        var evolved = CreateBat(_evolvedDir, "FR-ev-42.bat");
        var genome = Genome("FR-ev-42.bat", "FR-ev-42", null, StrategyOrigin.Evolved);

        Assert.Equal(evolved, AiOrchestratorService.FindExistingBatPath(genome, _engineDir));
    }

    [Fact]
    public void FindExistingBatPath_ReturnsNull_WhenNothingIsFound()
    {
        Assert.Null(AiOrchestratorService.FindExistingBatPath(
            Genome("more.bat", "more", null, StrategyOrigin.Evolved), _engineDir));
        Assert.Null(AiOrchestratorService.FindExistingBatPath(
            Genome(null, "more", null, StrategyOrigin.Evolved), _engineDir));
        Assert.Null(AiOrchestratorService.FindExistingBatPath(
            Genome(null, "builtin", @"C:\СтароеМесто\engine\builtin.bat"), _engineDir));
    }

    private string CreateBat(string dir, string fileName)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, "@echo off");
        return path;
    }

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
}
