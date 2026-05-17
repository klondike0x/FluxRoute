namespace FluxRoute.Core.Models;

public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public int ExplorationRatePermil { get; set; } = 100;
    public int MaxEvolvedStrategies { get; set; } = 24;
    public int EvolutionIntervalMinutes { get; set; } = 60;
    public int MinProbesBeforeEvolve { get; set; } = 6;
    public int KeepHistoryDays { get; set; } = 14;
}
