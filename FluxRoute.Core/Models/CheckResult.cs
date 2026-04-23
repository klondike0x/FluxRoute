namespace FluxRoute.Core.Models;

public sealed class CheckResult
{
    public string Key { get; init; } = "";
    public TargetKind Kind { get; init; }
    public string Value { get; init; } = "";
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
    public long? ElapsedMs { get; init; }
    public int? StatusCode { get; init; }
    public int? ExitCode { get; init; }
    public string Checker { get; init; } = "";
}
