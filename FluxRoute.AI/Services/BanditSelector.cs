using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class BanditSelector
{
    private readonly AiStrategyRegistry _registry;
    private readonly Random _rng;
    private readonly Dictionary<Guid, DateTimeOffset> _blockedUntil = new();
    private readonly Dictionary<string, DateTimeOffset> _sigCooldown = new();
    private readonly Dictionary<string, DateTimeOffset> _familyCooldown = new();
    private readonly Dictionary<Guid, int> _failureStreak = new();

    public BanditSelector(AiStrategyRegistry registry, Random? rng = null)
    {
        _registry = registry;
        _rng = rng ?? new Random();
    }

    public StrategyGenome? Pick(
        IReadOnlyList<StrategyGenome> candidates,
        string networkHash,
        int explorationPermil)
    {
        var now = DateTimeOffset.UtcNow;
        var usable = candidates.Where(g => !_blockedUntil.TryGetValue(g.Id, out var u) || u <= now).ToList();
        if (usable.Count == 0)
            return null;

        if (_rng.NextDouble() * 1000 < explorationPermil)
        {
            usable.Sort((a, b) =>
                _registry.SumPullsForGenomeOnNetwork(a.Id, networkHash)
                    .CompareTo(_registry.SumPullsForGenomeOnNetwork(b.Id, networkHash)));
            return usable[0];
        }

        var totalT = usable.Sum(g => 1 + _registry.SumPullsForGenomeOnNetwork(g.Id, networkHash));

        StrategyGenome? best = null;
        double bestScore = double.MinValue;

        foreach (var g in usable)
        {
            var entry = _registry.GetOrCreateBandit(g.Id, networkHash);
            var pulls = entry.Alpha + entry.Beta - 2;

            double alpha = entry.Alpha;
            double beta = entry.Beta;

            double sampleOrUcb;
            if (pulls < 1)
            {
                var agg = _registry.GetAggregatedBeta(g.Id);
                var apulls = agg.Alpha + agg.Beta - 2;
                if (apulls < 0.5)
                {
                    var mean = 0.5;
                    var n = 1.0;
                    sampleOrUcb = mean + Math.Sqrt(2 * Math.Log(totalT + 1) / n);
                }
                else
                {
                    sampleOrUcb = SampleBeta(agg.Alpha, agg.Beta);
                }
            }
            else
                sampleOrUcb = SampleBeta(alpha, beta);

            if (sampleOrUcb > bestScore)
            {
                bestScore = sampleOrUcb;
                best = g;
            }
        }

        return best;
    }

    public StrategyGenome? BestKnownForNetwork(IReadOnlyList<StrategyGenome> candidates, string networkHash)
    {
        StrategyGenome? best = null;
        double bestMean = -1;
        foreach (var g in candidates)
        {
            var entry = _registry.GetOrCreateBandit(g.Id, networkHash);
            var pulls = entry.Alpha + entry.Beta - 2;
            if (pulls < 1)
                continue;

            var mean = entry.Alpha / (entry.Alpha + entry.Beta);
            if (mean > bestMean)
            {
                bestMean = mean;
                best = g;
            }
        }

        return best;
    }

    public void RegisterSuccess(Guid genomeId)
    {
        _failureStreak.Remove(genomeId);
        _blockedUntil.Remove(genomeId);
    }

    public void RegisterFailure(StrategyGenome g, string? failureSignature)
    {
        var id = g.Id;
        _failureStreak.TryGetValue(id, out var streak);
        streak++;
        _failureStreak[id] = streak;

        var backoffMs = streak switch
        {
            1 => 300,
            2 => 700,
            3 => 1500,
            _ => 3000,
        };

        var jitter = 1 + (_rng.NextDouble() * 0.7 - 0.35);
        var delay = TimeSpan.FromMilliseconds(backoffMs * jitter);
        var until = DateTimeOffset.UtcNow + delay;

        if (_blockedUntil.TryGetValue(id, out var existing))
            until = until > existing ? until : existing;

        _blockedUntil[id] = until;

        var famKey = $"{id:N}|{g.DesyncMode}";
        _familyCooldown[famKey] = DateTimeOffset.UtcNow.AddSeconds(15);

        if (!string.IsNullOrEmpty(failureSignature))
        {
            var sigKey = $"{id:N}|{failureSignature}";
            _sigCooldown[sigKey] = DateTimeOffset.UtcNow.AddSeconds(15);
        }
    }

    public bool IsFamilyCooling(StrategyGenome g)
    {
        var famKey = $"{g.Id:N}|{g.DesyncMode}";
        return _familyCooldown.TryGetValue(famKey, out var u) && u > DateTimeOffset.UtcNow;
    }

    private double SampleBeta(double alpha, double beta)
    {
        var x = GammaSample(alpha);
        var y = GammaSample(beta);
        return x / (x + y + 1e-12);
    }

    private double GammaSample(double shape)
    {
        if (shape < 1e-9)
            return 1e-9;
        if (shape < 1)
            return GammaSample(shape + 1) * Math.Pow(_rng.NextDouble(), 1 / shape);

        var d = shape - 1.0 / 3;
        var c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x;
            do
            {
                x = NormalSample();
            } while (x <= -1 / c);

            var v = 1 + c * x;
            v = v * v * v;
            var u = _rng.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x)
                return d * v;

            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                return d * v;
        }
    }

    private double NormalSample()
    {
        var u1 = 1 - _rng.NextDouble();
        var u2 = 1 - _rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
