using FluxRoute.AI.Models;
using FluxRoute.AI.Services;

namespace FluxRoute.Core.Tests;

public sealed class AiStrategyRegistryTests
{
    [Fact]
    public void GetActiveGenomes_ExcludesDisabled()
    {
        var path = Path.Combine(Path.GetTempPath(), "fr-reg-" + Guid.NewGuid().ToString("N") + ".json");
        var reg = new AiStrategyRegistry(path);
        reg.Load();
        var a = new StrategyGenome { DisplayName = "a", DesyncMode = "split", FilterTcp = "80", SplitPosSemantic = "host" };
        var b = new StrategyGenome
        {
            DisplayName = "b",
            DesyncMode = "fake",
            FilterTcp = "443",
            FakeTlsMod = "rand",
            OrchestratorEnabled = false,
        };
        reg.Upsert(a);
        reg.Upsert(b);
        reg.Save();
        reg.Load();
        var active = reg.GetActiveGenomes();
        Assert.Single(active);
        Assert.Equal(a.Id, active[0].Id);
    }

    [Fact]
    public void SetOrchestratorEnabled_Persists()
    {
        var path = Path.Combine(Path.GetTempPath(), "fr-reg-" + Guid.NewGuid().ToString("N") + ".json");
        var reg = new AiStrategyRegistry(path);
        reg.Load();
        var a = new StrategyGenome { DisplayName = "a", DesyncMode = "split", FilterTcp = "80", SplitPosSemantic = "host" };
        reg.Upsert(a);
        reg.Save();
        reg.SetOrchestratorEnabled(a.Id, false);
        var reg2 = new AiStrategyRegistry(path);
        reg2.Load();
        Assert.False(reg2.GetById(a.Id)!.OrchestratorEnabled);
    }
}
