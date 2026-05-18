using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

public sealed class BatGenomeParserTests
{
    [Fact]
    public void FromLaunchPlan_ParsesKnownFlags_AndKeepsUnknownInExtra()
    {
        var args = new[]
        {
            "--filter-tcp", "80",
            "--dpi-desync", "fake",
            "--dpi-desync-split-pos", "midsld",
            "--dpi-desync-fake-tls-mod", "rand",
            "--unknown-flag", "x",
        };
        var plan = new WinwsLaunchPlan(@"C:\engine\bin\winws.exe", args, @"C:\engine", @"C:\engine\p.bat");
        var g = BatGenomeParser.FromLaunchPlan(plan, "test", StrategyOrigin.Builtin);

        Assert.Equal("80", g.FilterTcp);
        Assert.Equal("fake", g.DesyncMode);
        Assert.Equal("midsld", g.SplitPosSemantic);
        Assert.Equal("rand", g.FakeTlsMod);
        Assert.Contains("--unknown-flag", g.ExtraArgs);
        Assert.Contains("x", g.ExtraArgs);
    }

    [Fact]
    public void RoundTrip_ViaMaterializer_PreservesSignature()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fr-ai-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "bin"));
        Directory.CreateDirectory(Path.Combine(tmp, "lists"));

        var g = new StrategyGenome
        {
            DisplayName = "RT",
            DesyncMode = "split",
            FilterTcp = "80,443",
            SplitPosSemantic = "host",
            Origin = StrategyOrigin.Builtin,
        };
        var sig0 = GenomeSignature.Compute(g);

        var mat = new BatMaterializer();
        var built = mat.BuildBatContent(g, tmp);
        Assert.Contains("winws.exe", built);

        var args = BatMaterializer.BuildWinwsArgs(g);
        var plan = new WinwsLaunchPlan(Path.Combine(tmp, "bin", "winws.exe"), args, tmp, null);
        var g2 = BatGenomeParser.FromLaunchPlan(plan, "RT", StrategyOrigin.Builtin);
        Assert.Equal(sig0, GenomeSignature.Compute(g2));
    }
}
