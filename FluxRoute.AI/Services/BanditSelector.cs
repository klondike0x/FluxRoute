using System.Collections.Concurrent;
using FluxRoute.AI.Models;
using FluxRoute.Core.Models;

namespace FluxRoute.AI.Services;

public sealed class BanditSelector
{
    private readonly AiStrategyRegistry _registry;
    private readonly Func<AiSettings> _aiSettings;
    private readonly Random _rng;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _blockedUntil = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sigCooldown = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _familyCooldown = new();
    private readonly ConcurrentDictionary<Guid, int> _failureStreak = new();

    public BanditSelector(AiStrategyRegistry registry, Func<AiSettings>? aiSettings = null, Random? rng = null)
    {
        _registry = registry;
        _aiSettings = aiSettings ?? (() => new AiSettings());
        _rng = rng ?? new Random();
    }

    public StrategyGenome? Pick(
        IReadOnlyList<StrategyGenome> candidates,
        string networkHash,
        int explorationPermil)
    {
        var now = DateTimeOffset.UtcNow;
        var usable = candidates.Where(g =>
            !_blockedUntil.TryGetValue(g.Id, out var u) || u <= now).ToList();
        if (usable.Count == 0)
            return null;

        if (_rng.NextDouble() * 1000 < explorationPermil)
        {
            usable.Sort((a, b) =>
                _registry.SumPullsForGenomeOnNetwork(a.Id, networkHash)
                    .CompareTo(_registry.SumPullsForGenomeOnNetwork(b.Id, networkHash)));
            return usable[0];
        }

        var effective = _aiSettings().ParetoEnabled
            ? ParetoFront(usable, networkHash)
            : usable;
        if (effective.Count == 0) effective = usable;

        var totalT = effective.Sum(g => 1 + _registry.SumPullsForGenomeOnNetwork(g.Id, networkHash));

        StrategyGenome? best = null;
        double bestScore = double.MinValue;

        foreach (var g in effective)
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

    public List<StrategyGenome> ParetoFront(IReadOnlyList<StrategyGenome> candidates, string networkHash)
    {
        var scored = candidates
            .Select(g =>
            {
                var entry = _registry.GetOrCreateBandit(g.Id, networkHash);
                var pulls = entry.Alpha + entry.Beta - 2;
                if (pulls < 1) return (g, score: 0.5, latency: 1000.0);
                var score = entry.Alpha / (entry.Alpha + entry.Beta);
                var latency = entry.Alpha > 0 ? entry.Alpha / (entry.Alpha + entry.Beta) * 100 : 500;
                return (g, score, latency);
            })
            .ToList();

        var pareto = new List<(StrategyGenome g, double score, double latency)>();
        foreach (var item in scored)
        {
            bool dominated = false;
            foreach (var other in scored)
            {
                if (other.g.Id == item.g.Id) continue;
                if (other.score >= item.score && other.latency <= item.latency &&
                    (other.score > item.score || other.latency < item.latency))
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated) pareto.Add(item);
        }

        return pareto.Select(x => x.g).ToList();
    }

    public void RegisterSuccess(Guid genomeId)
    {
        _failureStreak.TryRemove(genomeId, out _);
        _blockedUntil.TryRemove(genomeId, out _);
    }

    public void RegisterFailure(StrategyGenome g, string? failureSignature)
    {
        var id = g.Id;
        var streak = _failureStreak.AddOrUpdate(id, 1, (_, s) => s + 1);

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

        _blockedUntil.AddOrUpdate(id, until, (_, existing) => until > existing ? until : existing);

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
