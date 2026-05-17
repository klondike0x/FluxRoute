using FluxRoute.Core.Models;
using FluxRoute.AI.Stats;
using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class StrategyEvolver
{
    private static readonly string[] SemanticMarkers = ["host", "endhost", "midsld", "sniext", "endsld"];
    private static readonly string[] DesyncModes = ["split", "fake", "fakesplit", "disorder", "fakedisorder", "multidisorder", "multisplit"];
    private static readonly string[] FakeTlsMods = ["orig", "rand", "rndsni", "dupsid", "padencap"];

    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly BatMaterializer _materializer;
    private readonly Random _rng;
    private readonly Func<string> _engineDir;
    private readonly Func<AiSettings> _aiSettings;

    public StrategyEvolver(
        AiStrategyRegistry registry,
        AiHistoryStore history,
        BatMaterializer materializer,
        Func<string> engineDir,
        Func<AiSettings> aiSettings,
        Random? rng = null)
    {
        _registry = registry;
        _history = history;
        _materializer = materializer;
        _engineDir = engineDir;
        _aiSettings = aiSettings;
        _rng = rng ?? new Random();
    }

    public StrategyGenome? Evolve(NetworkFingerprint net)
    {
        var pool = _registry.GetActiveGenomes().ToList();
        if (pool.Count < 2)
            return null;

        var outcomes = _history.LoadForNetwork(net.Hash);
        var byGenome = outcomes.GroupBy(o => o.GenomeId).ToDictionary(g => g.Key, g => g.ToList());

        var scored = pool
            .Select(g =>
            {
                byGenome.TryGetValue(g.Id, out var list);
                list ??= [];
                var succ = list.Count(o => o.Score >= 50);
                var trials = list.Count;
                var wilson = WilsonScore.LowerBound(succ, trials);
                return (g, wilson, trials);
            })
            .OrderByDescending(x => x.wilson)
            .ThenByDescending(x => x.trials)
            .ToList();

        var parents = scored.Take(Math.Min(6, scored.Count)).Select(x => x.g).ToList();
        if (parents.Count < 2)
            parents = pool.OrderBy(_ => _rng.Next()).Take(Math.Min(4, pool.Count)).ToList();

        if (parents.Count < 2)
            return null;

        StrategyGenome? child = null;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var p0 = parents[_rng.Next(parents.Count)];
            var p1 = parents[_rng.Next(parents.Count)];
            if (p0.Id == p1.Id && parents.Count > 1)
            {
                var idx = parents.IndexOf(p0);
                p1 = parents[(idx + 1) % parents.Count];
            }

            child = Crossover(p0, p1);
            Mutate(child);
            StrategyGenomeValidator.Normalize(child);

            if (!StrategyGenomeValidator.IsValid(child))
                continue;

            var sig = GenomeSignature.Compute(child);
            if (pool.Any(x => GenomeSignature.Compute(x) == sig))
                continue;

            break;
        }

        if (child is null || !StrategyGenomeValidator.IsValid(child))
            return null;

        _registry.GenerationCounter++;
        child.Generation = _registry.GenerationCounter;
        child.Origin = StrategyOrigin.Evolved;
        child.Id = Guid.NewGuid();
        child.CreatedAt = DateTimeOffset.UtcNow;
        child.DisplayName = SanitizeDisplay($"FR-ev-{child.Generation}-{_rng.Next(1000, 9999)}");
        child.SourceBatPath = null;
        child.BatFileName = null;

        var dir = _engineDir();
        var path = _materializer.WriteBat(child, dir);
        child.SourceBatPath = path;
        child.BatFileName = Path.GetFileName(path);

        _registry.Upsert(child);
        GarbageCollectEvolved();
        _registry.Save();

        return child;
    }

    private void GarbageCollectEvolved()
    {
        var settingsMax = Math.Max(4, _aiSettings().MaxEvolvedStrategies);
        var evolved = _registry.GetGenomes().Where(g => g.Origin == StrategyOrigin.Evolved).ToList();
        if (evolved.Count <= settingsMax)
            return;

        var allOutcomes = _history.LoadAll();
        var byGenome = allOutcomes.GroupBy(o => o.GenomeId).ToDictionary(g => g.Key, g => g.ToList());

        var ranked = evolved
            .Select(g =>
            {
                byGenome.TryGetValue(g.Id, out var list);
                list ??= [];
                var succ = list.Count(o => o.Score >= 50);
                var trials = list.Count;
                var w = WilsonScore.LowerBound(succ, trials);
                return (g, w);
            })
            .OrderBy(x => x.w)
            .ToList();

        var removeCount = evolved.Count - settingsMax;
        foreach (var (g, _) in ranked.Take(removeCount))
        {
            TryDeleteBat(g);
            _registry.Remove(g.Id);
        }
    }

    private void TryDeleteBat(StrategyGenome g)
    {
        try
        {
            if (!string.IsNullOrEmpty(g.SourceBatPath) && File.Exists(g.SourceBatPath))
                File.Delete(g.SourceBatPath);
        }
        catch
        {
        }
    }

    private StrategyGenome Crossover(StrategyGenome a, StrategyGenome b)
    {
        return new StrategyGenome
        {
            FilterTcp = RngPick(a.FilterTcp, b.FilterTcp),
            FilterUdp = RngPick(a.FilterUdp, b.FilterUdp),
            DesyncMode = RngPick(a.DesyncMode, b.DesyncMode),
            SplitPos = RngPickNullableStruct(a.SplitPos, b.SplitPos),
            SplitPosSemantic = RngPickNullableRef(a.SplitPosSemantic, b.SplitPosSemantic),
            FakeTtl = RngPickNullableStruct(a.FakeTtl, b.FakeTtl),
            AutoTtl = RngPickBool(a.AutoTtl, b.AutoTtl),
            FakeTlsMod = RngPickNullableRef(a.FakeTlsMod, b.FakeTlsMod),
            Hostlist = RngPickNullableRef(a.Hostlist, b.Hostlist),
            RepeatCount = RngPickNullableStruct(a.RepeatCount, b.RepeatCount),
            ExtraArgs = RngPickList(a.ExtraArgs, b.ExtraArgs),
            ParentIds = [a.Id, b.Id],
        };
    }

    private string RngPick(string x, string y) => _rng.Next(2) == 0 ? x : y;

    private T? RngPickNullableStruct<T>(T? x, T? y) where T : struct =>
        _rng.Next(2) == 0 ? x : y;

    private string? RngPickNullableRef(string? x, string? y) =>
        _rng.Next(2) == 0 ? x : y;

    private bool RngPickBool(bool x, bool y) =>
        _rng.Next(2) == 0 ? x : y;

    private List<string> RngPickList(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0)
            return [.. b];
        if (b.Count == 0)
            return [.. a];
        return _rng.Next(2) == 0 ? [.. a] : [.. b];
    }

    private void Mutate(StrategyGenome g)
    {
        switch (_rng.Next(6))
        {
            case 0:
                g.SplitPosSemantic = SemanticMarkers[_rng.Next(SemanticMarkers.Length)];
                g.SplitPos = null;
                break;
            case 1:
                g.DesyncMode = DesyncModes[_rng.Next(DesyncModes.Length)];
                break;
            case 2:
                if (g.FakeTtl is null)
                    g.FakeTtl = 6 + _rng.Next(10);
                else
                    g.FakeTtl = Math.Clamp(g.FakeTtl.Value + PickDelta(), 3, 48);
                break;
            case 3:
                g.FakeTlsMod = FakeTlsMods[_rng.Next(FakeTlsMods.Length)];
                break;
            case 4:
                g.AutoTtl = !g.AutoTtl;
                break;
            default:
                if (g.SplitPosSemantic is not null)
                    g.SplitPos = _rng.Next(16, 180);
                g.SplitPosSemantic = null;
                break;
        }
    }

    private int PickDelta()
    {
        var d = new[] { 1, 2, 4, 8 };
        var v = d[_rng.Next(d.Length)];
        return _rng.Next(2) == 0 ? v : -v;
    }

    private static string SanitizeDisplay(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(arr);
    }
}
