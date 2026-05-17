namespace FluxRoute.AI.Models;

public sealed class ProbeOutcome
{
    public Guid GenomeId { get; set; }
    public string NetworkHash { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public int Score { get; set; }
    public double SuccessRate { get; set; }
    public double AvgLatencyMs { get; set; }
    public bool ProcessStable { get; set; }
    public List<string> FailedTargetKeys { get; set; } = [];
    public string? FailureSignature { get; set; }
}
