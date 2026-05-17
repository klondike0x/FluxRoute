using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public static class StrategyGenomeValidator
{
    private static readonly HashSet<string> FakeModes =
        ["fake", "fakesplit", "fakedisorder", "multidisorder", "multisplit"];

    public static bool IsValid(StrategyGenome g)
    {
        if (string.IsNullOrWhiteSpace(g.DesyncMode))
            return false;

        if (!string.IsNullOrEmpty(g.FakeTlsMod) && !FakeModes.Contains(g.DesyncMode.ToLowerInvariant()))
            return false;

        return true;
    }

    public static void Normalize(StrategyGenome g)
    {
        if (!string.IsNullOrEmpty(g.FakeTlsMod) && !FakeModes.Contains(g.DesyncMode.ToLowerInvariant()))
            g.FakeTlsMod = null;

        if (g.FakeTtl is < 1 or > 128)
            g.FakeTtl = null;
    }

    public static StrategyGenome Clone(StrategyGenome g)
    {
        return new StrategyGenome
        {
            Id = Guid.NewGuid(),
            ParentIds = [.. g.ParentIds],
            Generation = g.Generation,
            Origin = g.Origin,
            FilterTcp = g.FilterTcp,
            FilterUdp = g.FilterUdp,
            DesyncMode = g.DesyncMode,
            SplitPos = g.SplitPos,
            SplitPosSemantic = g.SplitPosSemantic,
            FakeTtl = g.FakeTtl,
            AutoTtl = g.AutoTtl,
            FakeTlsMod = g.FakeTlsMod,
            Hostlist = g.Hostlist,
            RepeatCount = g.RepeatCount,
            ExtraArgs = [.. g.ExtraArgs],
            DisplayName = g.DisplayName,
            BatFileName = g.BatFileName,
            SourceBatPath = g.SourceBatPath,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
