using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IDohSelectionService
{
    IReadOnlyList<DohProviderTestResult> Rank(IEnumerable<DohProviderTestResult> results);
    DohProviderTestResult? SelectBest(IEnumerable<DohProviderTestResult> results);
}

/// <summary>Ранжирует DoH-провайдеров по стабильности и задержке.</summary>
public sealed class DohSelectionService : IDohSelectionService
{
    public IReadOnlyList<DohProviderTestResult> Rank(IEnumerable<DohProviderTestResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Select(result => result with { Score = CalculateScore(result) })
            .OrderByDescending(result => result.IsAvailable)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.AverageLatencyMs ?? double.MaxValue)
            .ThenBy(result => result.Provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DohProviderTestResult? SelectBest(IEnumerable<DohProviderTestResult> results)
    {
        return Rank(results).FirstOrDefault(result => result.IsAvailable);
    }

    private static double CalculateScore(DohProviderTestResult result)
    {
        if (!result.IsAvailable)
        {
            return 0;
        }

        var latencyScore = 1d / (1d + result.AverageLatencyMs!.Value / 100d);
        return Math.Round((result.SuccessRate * 0.75d) + (latencyScore * 0.25d), 4);
    }
}
