namespace FluxRoute.AI.Models;

public sealed class NetworkFingerprint
{
    public string Hash { get; init; } = "";
    public string Label { get; init; } = "";
    public string Transport { get; init; } = "";
    public string GatewayIp { get; init; } = "";
    public List<string> DnsServers { get; init; } = [];
    public string LocalSubnet { get; init; } = "";
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
