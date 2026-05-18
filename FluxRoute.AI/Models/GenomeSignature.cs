using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxRoute.AI.Models;

public static class GenomeSignature
{
    public static string Compute(StrategyGenome g)
    {
        var payload = new
        {
            g.FilterTcp,
            g.FilterUdp,
            g.DesyncMode,
            g.SplitPos,
            g.SplitPosSemantic,
            g.FakeTtl,
            g.AutoTtl,
            g.FakeTlsMod,
            g.Hostlist,
            g.RepeatCount,
            Extra = string.Join('\u001f', g.ExtraArgs),
        };
        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }
}
