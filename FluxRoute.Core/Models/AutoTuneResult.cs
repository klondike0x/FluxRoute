using System;

namespace FluxRoute.Core.Models;

/// <summary>
/// Результат тестирования одной комбинации настроек в Auto-Tune.
/// </summary>
public sealed class AutoTuneResult
{
    public string IpSetMode { get; init; } = "";
    public string GameFilterProtocol { get; init; } = "";
    public int SuccessCount { get; init; }
    public int TotalCount { get; init; }
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
    public double AvgLatencyMs { get; init; }
    public double MinLatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
    public TimeSpan TestDuration { get; init; }
    public bool IsPerfect => SuccessCount == TotalCount && TotalCount > 0;
    public int CompositeScore => CalculateCompositeScore();

    private int CalculateCompositeScore()
    {
        if (TotalCount == 0) return 0;

        // 60% — доля успешных проверок
        var successScore = (int)(SuccessRate * 0.6);

        // 30% — средняя задержка (чем меньше, тем лучше)
        var latencyScore = (int)Math.Max(0, (1 - Math.Min(AvgLatencyMs, 2000) / 2000) * 30);

        // 10% — стабильность (разброс задержек)
        var stability = MaxLatencyMs > 0 && MinLatencyMs > 0
            ? 1 - (MaxLatencyMs - MinLatencyMs) / MaxLatencyMs
            : 1;
        var stabilityScore = (int)(stability * 10);

        return successScore + latencyScore + stabilityScore;
    }

    public string DisplayText =>
        $"{IpSetMode} / {(GameFilterProtocol == "Выкл" ? "без фильтра" : GameFilterProtocol)}: " +
        $"{SuccessCount}/{TotalCount} ({SuccessRate:0.#}%), {AvgLatencyMs:0} мс";
}