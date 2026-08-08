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

        var sel = new BanditSelector(reg, new Random(42));
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
        var sel = new BanditSelector(reg, new Random(1));
        var p = sel.Pick([g], "x", explorationPermil: 1000);
        Assert.Same(g, p);
    }

    /// <summary>
    /// Исправление #62: холодный старт не должен всегда возвращать первую стратегию.
    /// При отсутствии истории все кандидаты должны получать случайные оценки (Uniform sampling),
    /// а не детерминированно одинаковый UCB.
    /// </summary>
    [Fact]
    public void Pick_ColdStart_ReturnsDifferentStrategies()
    {
        var path = Path.Combine(Path.GetTempPath(), "fr-ai-cs-" + Guid.NewGuid().ToString("N") + ".json");
        var reg = new AiStrategyRegistry(path);
        reg.Load();

        // Три стратегии без какой-либо истории
        var g1 = new StrategyGenome { DisplayName = "alpha", DesyncMode = "split" };
        var g2 = new StrategyGenome { DisplayName = "beta", DesyncMode = "fake" };
        var g3 = new StrategyGenome { DisplayName = "gamma", DesyncMode = "disorder" };
        reg.Upsert(g1);
        reg.Upsert(g2);
        reg.Upsert(g3);

        var sel = new BanditSelector(reg, new Random(42));
        var list = new List<StrategyGenome> { g1, g2, g3 };
        var counts = new Dictionary<Guid, int> { [g1.Id] = 0, [g2.Id] = 0, [g3.Id] = 0 };

        // При холодном старте без exploration (explorationPermil=0)
        // выбор должен распределяться между стратегиями
        for (var i = 0; i < 200; i++)
        {
            var p = sel.Pick(list, "cold-net", explorationPermil: 0);
            Assert.NotNull(p);
            counts[p.Id]++;
        }

        // Каждая стратегия должна быть выбрана хотя бы 30 раз из 200
        // (вероятность что Uniform sampling никогда не выберет одну из трёх ~ (2/3)^200 ≈ 10^-35)
        Assert.True(counts[g1.Id] >= 30, $"g1 only picked {counts[g1.Id]} times");
        Assert.True(counts[g2.Id] >= 30, $"g2 only picked {counts[g2.Id]} times");
        Assert.True(counts[g3.Id] >= 30, $"g3 only picked {counts[g3.Id]} times");
    }
}
