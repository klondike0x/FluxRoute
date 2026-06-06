using FluxRoute.Core.Models;

namespace FluxRoute.Core.Models;

public sealed class EngineProfile
{
    public DpiEngineType EngineType { get; set; } = DpiEngineType.Zapret;
    public int SocksPort { get; set; } = 1080;

    public string FilterTcp { get; set; } = "";
    public string FilterUdp { get; set; } = "";

    public string DesyncMode { get; set; } = "split";

    public string? SplitPos { get; set; }
    public string? DisorderPos { get; set; }
    public string? FakePos { get; set; }
    public string? OobPos { get; set; }
    public string? DisoobPos { get; set; }
    public string? TlsrecPos { get; set; }

    public int? FakeTtl { get; set; }
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
}
