using System.Text.Json.Serialization;
using FluxRoute.Core.Models;

namespace FluxRoute.AI.Models;

public sealed class StrategyGenome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Guid> ParentIds { get; set; } = [];
    public int Generation { get; set; }
    public StrategyOrigin Origin { get; set; } = StrategyOrigin.Builtin;

    public DpiEngineType EngineType { get; set; } = DpiEngineType.Zapret;

    public string FilterTcp { get; set; } = "";
    public string FilterUdp { get; set; } = "";
    public string DesyncMode { get; set; } = "split";

    public int? SplitPos { get; set; }
    public string? SplitPosSemantic { get; set; }

    public string? DisorderPos { get; set; }
    public string? FakePos { get; set; }
    public string? OobPos { get; set; }
    public string? DisoobPos { get; set; }
    public string? TlsrecPos { get; set; }

    public int? FakeTtl { get; set; }
    public bool AutoTtl { get; set; }
    public bool? Md5sig { get; set; }
    public string? FakeTlsMod { get; set; }
    public string? FakeSni { get; set; }
    public string? FakeData { get; set; }
    public string? ModHttp { get; set; }
    public int? Tlsminor { get; set; }
    public string? Hosts { get; set; }
    public string? Hostlist { get; set; }
    public int? RepeatCount { get; set; }
    public int? CacheTtl { get; set; }
    public string? Auto { get; set; }
    public int? Timeout { get; set; }
    public int? AutoMode { get; set; }

    public string? DesyncAnyProtocol { get; set; }
    public string? DesyncFooling { get; set; }
    public string? FakeResend { get; set; }

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

    public EngineProfile ToEngineProfile(int socksPort = 1080)
    {
        return new EngineProfile
        {
            EngineType = EngineType,
            SocksPort = socksPort,
            FilterTcp = FilterTcp,
            FilterUdp = FilterUdp,
            DesyncMode = DesyncMode,
            SplitPos = SplitPosSemantic ?? SplitPos?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DisorderPos = DisorderPos,
            FakePos = FakePos,
            OobPos = OobPos,
            DisoobPos = DisoobPos,
            TlsrecPos = TlsrecPos,
            FakeTtl = FakeTtl,
            Md5sig = Md5sig,
            FakeTlsMod = FakeTlsMod,
            FakeSni = FakeSni,
            FakeData = FakeData,
            ModHttp = ModHttp,
            Tlsminor = Tlsminor,
            Hosts = Hosts,
            Hostlist = Hostlist,
            RepeatCount = RepeatCount,
            CacheTtl = CacheTtl,
            Auto = Auto,
            Timeout = Timeout,
            AutoMode = AutoMode,
            DesyncAnyProtocol = DesyncAnyProtocol,
            DesyncFooling = DesyncFooling,
            FakeResend = FakeResend,
            ExtraArgs = [..ExtraArgs],
        };
    }
}
