using System.Text.Json.Serialization;

namespace FluxRoute.AI.Models;

public sealed class StrategyGenome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Guid> ParentIds { get; set; } = [];
    public int Generation { get; set; }
    public StrategyOrigin Origin { get; set; } = StrategyOrigin.Builtin;
    public string FilterTcp { get; set; } = "";
    public string FilterUdp { get; set; } = "";
    public string DesyncMode { get; set; } = "split";
    public int? SplitPos { get; set; }
    public string? SplitPosSemantic { get; set; }
    public int? FakeTtl { get; set; }
    public bool AutoTtl { get; set; }
    public string? FakeTlsMod { get; set; }
    public string? Hostlist { get; set; }
    public int? RepeatCount { get; set; }
    public List<string> ExtraArgs { get; set; } = [];
    public string DisplayName { get; set; } = "";
    public string? BatFileName { get; set; }
    public string? SourceBatPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool OrchestratorEnabled { get; set; } = true;
    public int? LastVerificationScore { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }

    [JsonIgnore]
    public string Signature => GenomeSignature.Compute(this);
}
