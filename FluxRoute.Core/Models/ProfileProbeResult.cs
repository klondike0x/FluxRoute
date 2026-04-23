namespace FluxRoute.Core.Models;

public sealed class ProfileProbeResult
{
    public ProfileItem? Profile { get; init; }
    public string ProfileName => Profile?.DisplayName ?? "—";
    public bool ProcessStarted { get; init; }
    public bool ProcessStable { get; init; }
    public IReadOnlyList<int> ProcessIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<CheckResult> Checks { get; init; } = Array.Empty<CheckResult>();
    public double SuccessRate { get; init; }
    public int Score { get; init; }
    public TimeSpan Duration { get; init; }
    public string Summary { get; init; } = "";

    public bool IsWorking(double threshold) => Score >= (int)Math.Round(threshold * 100);

    public IEnumerable<CheckResult> FailedChecks => Checks.Where(x => !x.Ok);

    public string ShortProcessText
    {
        get
        {
            if (!ProcessStarted) return "winws.exe не найден";
            if (!ProcessStable) return "winws.exe запущен, но нестабилен";
            return ProcessIds.Count == 0
                ? "winws.exe запущен"
                : $"winws.exe OK, PID: {string.Join(", ", ProcessIds.Take(3))}";
        }
    }

    public IReadOnlyList<string> GetDetailLines()
    {
        var lines = new List<string>
        {
            ProcessStable ? $"✅ {ShortProcessText}" : $"❌ {ShortProcessText}"
        };

        foreach (var check in Checks.OrderByDescending(x => x.Ok).ThenBy(x => x.Key))
        {
            var icon = check.Ok ? "✅" : "❌";
            var elapsed = check.ElapsedMs is null ? "" : $", {check.ElapsedMs} мс";
            var checker = string.IsNullOrWhiteSpace(check.Checker) ? "" : $" [{check.Checker}]";
            lines.Add($"{icon} {check.Key}: {check.Detail}{elapsed}{checker}");
        }

        return lines;
    }
}
