using System.Collections.Generic;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;

namespace FluxRoute.Core.Tests;

public sealed class BanditSelectorTests
{
    [Fact]
    public void Pick_WithZeroExploration_PrefersHigherPosteriorMean()
    {
        var path = Path.Combine(Path.GetTempPath(), "fr-ai-bd-" + Guid.NewGuid().ToString("N") + ".json");
        var reg = new AiStrategyRegistry(path);
        reg.Load();

        var gBetter = new StrategyGenome { DisplayName = "a", DesyncMode = "split" };
        var gWorse = new StrategyGenome { DisplayName = "b", DesyncMode = "split" };
        reg.Upsert(gBetter);
        reg.Upsert(gWorse);

        const string net = "nh";
        reg.RecordBanditSuccess(gBetter.Id, net);
        reg.RecordBanditSuccess(gBetter.Id, net);
        reg.RecordBanditSuccess(gBetter.Id, net);
        reg.RecordBanditFailure(gWorse.Id, net);

        var sel = new BanditSelector(reg, aiSettings: null, new Random(42));
        var counts = new Dictionary<Guid, int>
        {
            [gBetter.Id] = 0,
            [gWorse.Id] = 0,
        };

        var list = new List<StrategyGenome> { gBetter, gWorse };
        for (var i = 0; i < 400; i++)
        {
            var p = sel.Pick(list, net, explorationPermil: 0);
            Assert.NotNull(p);
            counts[p.Id]++;
        }

        Assert.True(counts[gBetter.Id] > counts[gWorse.Id]);
    }

    [Fact]
    public void Pick_WithFullExploration_ReturnsCandidate()
    {
        var path = Path.Combine(Path.GetTempPath(), "fr-ai-bd2-" + Guid.NewGuid().ToString("N") + ".json");
        var reg = new AiStrategyRegistry(path);
        reg.Load();
        var g = new StrategyGenome { DisplayName = "only", DesyncMode = "split" };
        reg.Upsert(g);
        var sel = new BanditSelector(reg, aiSettings: null, new Random(1));
        var p = sel.Pick([g], "x", explorationPermil: 1000);
        Assert.Same(g, p);
    }
}
