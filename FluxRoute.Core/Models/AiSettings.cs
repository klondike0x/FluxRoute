namespace FluxRoute.Core.Models;

public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public int ExplorationRatePermil { get; set; } = 100;
    public int MaxEvolvedStrategies { get; set; } = 24;
    public int EvolutionIntervalMinutes { get; set; } = 60;
    public int MinProbesBeforeEvolve { get; set; } = 6;
    public int KeepHistoryDays { get; set; } = 14;
    public bool FastStartEnabled { get; set; } = true;
    public int NetworkCacheSize { get; set; } = 20;
    public bool ParetoEnabled { get; set; } = true;
    public bool ElitismEnabled { get; set; } = true;

    public int EngineMode { get; set; } = 0;
    public bool UseHybridMode { get; set; }
    public int ByeDpiSocksPort { get; set; } = 1080;

    public ByeDpiProfileSettings ByeDpiDefaults { get; set; } = new();

    /// <summary>Порог процента успеха, ниже которого эволюционированная стратегия автоматически удаляется.</summary>
    public int AutoDeleteBelowScore { get; set; } = 60;
}

public sealed class ByeDpiProfileSettings
{
    public int SocksPort { get; set; } = 1080;
    public string? SplitPos { get; set; }
    public string? DisorderPos { get; set; } = "1";
    public string? FakePos { get; set; }
    public string? OobPos { get; set; }
    public string? TlsrecPos { get; set; }
    public int? FakeTtl { get; set; }
    public bool AutoTtl { get; set; }
    public string? Auto { get; set; } = "torst";
    public int? Timeout { get; set; }
    public string? Hosts { get; set; }
    public string? Hostlist { get; set; }
    public string? FakeTlsMod { get; set; }
    public string? FakeSni { get; set; }
    public string? FakeData { get; set; }
    public string? ModHttp { get; set; }
    public int? Tlsminor { get; set; }
    public bool Md5sig { get; set; }
}
