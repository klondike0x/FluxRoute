// ═══ v1.7.0: SiteDiagnosticResult ═══
namespace FluxRoute.Core.Models;

public class SiteDiagnosticResult
{
    public string Site { get; set; } = "";
    public bool IsAccessible { get; set; }
    public bool DpiBlocked { get; set; }
    public bool IpBlocked { get; set; }
    public int? StatusCode { get; set; }
    public long? LatencyMs { get; set; }
    public string? Detail { get; set; }
}
