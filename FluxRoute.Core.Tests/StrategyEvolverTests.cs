using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Tests;

public sealed class StrategyEvolverTests
{
    [Fact]
    public void Evolve_CreatesChild_WithEvolvedOrigin()
    {
        var root = Path.Combine(Path.GetTempPath(), "fr-ai-ev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "lists"));

        var regPath = Path.Combine(root, "strategies.json");
        var histPath = Path.Combine(root, "history.jsonl");
        var reg = new AiStrategyRegistry(regPath);
        reg.Load();
        var hist = new AiHistoryStore(histPath);

        var g1 = new StrategyGenome { DisplayName = "p1", DesyncMode = "split", FilterTcp = "80", SplitPosSemantic = "host" };
        var g2 = new StrategyGenome { DisplayName = "p2", DesyncMode = "fake", FilterTcp = "443", FakeTlsMod = "rand" };
        var g3 = new StrategyGenome
        {
            DisplayName = "p3",
            DesyncMode = "multisplit",
            FilterTcp = "443",
            SplitPosSemantic = "host",
            OrchestratorEnabled = false,
        };
        reg.Upsert(g1);
        reg.Upsert(g2);
        reg.Upsert(g3);

        var net = new NetworkFingerprint { Hash = "nh1", Label = "t" };
        for (var i = 0; i < 8; i++)
        {
            hist.Append(new ProbeOutcome
            {
                GenomeId = g1.Id,
                NetworkHash = net.Hash,
                Score = 80,
                SuccessRate = 1,
                AvgLatencyMs = 10,
                ProcessStable = true,
            });
        }

        var evolver = new StrategyEvolver(
            reg,
            hist,
            () => root,
            () => new AiSettings { MaxEvolvedStrategies = 24 },
            new Random(777));

        var child = evolver.Evolve(net);
        Assert.NotNull(child);
        Assert.Equal(StrategyOrigin.Evolved, child!.Origin);
        Assert.Null(child.SourceBatPath);
        Assert.Null(child.BatFileName);
        Assert.True(reg.GetGenomes().Any(x => x.Id == child.Id));
    }
}
