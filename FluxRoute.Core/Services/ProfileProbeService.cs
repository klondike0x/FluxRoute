using System.Diagnostics;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class ProfileProbeService
{
    private readonly Func<ProfileItem?, Task>? _switchProfile;
    private readonly IConnectivityChecker _connectivity;

    public ProfileProbeService(IConnectivityChecker connectivity, Func<ProfileItem?, Task>? switchProfile = null)
    {
        _connectivity = connectivity;
        _switchProfile = switchProfile;
    }

    public async Task<ProfileProbeResult> ProbeAsync(
        ProfileItem profile,
        IEnumerable<TargetEntry> targets,
        ProfileProbeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ProfileProbeOptions();
        var sw = Stopwatch.StartNew();

        try
        {
            if (_switchProfile is not null)
                await _switchProfile(profile).ConfigureAwait(false);

            if (options.StartupWait > TimeSpan.Zero)
                await Task.Delay(options.StartupWait, ct).ConfigureAwait(false);

            return await ProbeCurrentCoreAsync(_connectivity, profile, targets, options, sw, ct).ConfigureAwait(false);
        }
        finally
        {
            if (options.StopAfterProbe && _switchProfile is not null && !ct.IsCancellationRequested)
            {
                try
                {
                    await _switchProfile(null).ConfigureAwait(false);
                }
                catch
                {
                    // Остановка после теста не должна ломать весь цикл сканирования.
                }
            }
        }
    }

    public async Task<ProfileProbeResult> ProbeCurrentAsync(
        ProfileItem? profile,
        IEnumerable<TargetEntry> targets,
        ProfileProbeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ProfileProbeOptions
        {
            StartupWait = TimeSpan.Zero,
            ProcessWaitTimeout = TimeSpan.FromSeconds(2),
            StableWait = TimeSpan.FromMilliseconds(500),
            StopAfterProbe = false
        };

        var sw = Stopwatch.StartNew();
        return await ProbeCurrentCoreAsync(_connectivity, profile, targets, options, sw, ct).ConfigureAwait(false);
    }

    private static async Task<ProfileProbeResult> ProbeCurrentCoreAsync(
        IConnectivityChecker connectivity,
        ProfileItem? profile,
        IEnumerable<TargetEntry> targets,
        ProfileProbeOptions options,
        Stopwatch sw,
        CancellationToken ct)
    {
        var targetList = targets
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToList();

        var firstSnapshot = await ProcessHealthChecker.WaitForProcessAsync(
            options.ProcessName,
            options.ProcessWaitTimeout,
            TimeSpan.FromMilliseconds(250),
            ct).ConfigureAwait(false);

        var processStarted = firstSnapshot.IsRunning;
        var processStable = false;
        IReadOnlyList<int> processIds = firstSnapshot.ProcessIds;

        if (processStarted)
        {
            if (options.StableWait > TimeSpan.Zero)
                await Task.Delay(options.StableWait, ct).ConfigureAwait(false);

            var stableSnapshot = ProcessHealthChecker.Snapshot(options.ProcessName);
            processStable = stableSnapshot.IsRunning &&
                            (firstSnapshot.ProcessIds.Count == 0 ||
                             firstSnapshot.ProcessIds.Any(id => stableSnapshot.ProcessIds.Contains(id)) ||
                             stableSnapshot.ProcessIds.Count > 0);
            processIds = stableSnapshot.ProcessIds;
        }

        var (successRate, checks) = await connectivity.CheckAllAsync(
            targetList,
            options.UseCurlForHttp,
            options.MaxParallelChecks,
            ct).ConfigureAwait(false);
        var score = ProfileScoringService.Calculate(processStarted, processStable, checks, options.RequireWinwsProcess);

        sw.Stop();

        return new ProfileProbeResult
        {
            Profile = profile,
            ProcessStarted = processStarted,
            ProcessStable = processStable,
            ProcessIds = processIds,
            Checks = checks,
            SuccessRate = successRate,
            Score = score,
            Duration = sw.Elapsed,
            Summary = BuildSummary(processStarted, processStable, checks)
        };
    }

    private static string BuildSummary(bool processStarted, bool processStable, IReadOnlyList<CheckResult> checks)
    {
        if (!processStarted)
            return "winws.exe не запущен";

        if (!processStable)
            return "winws.exe завершился или нестабилен";

        if (checks.Count == 0)
            return "winws.exe OK, но цели проверки не заданы";

        var failed = checks.Where(x => !x.Ok).Select(x => x.Key).Take(3).ToList();
        var okCount = checks.Count(x => x.Ok);
        var avgLatency = checks
            .Where(x => x.Ok && x.ElapsedMs is not null)
            .Select(x => x.ElapsedMs!.Value)
            .DefaultIfEmpty(0)
            .Average();

        if (failed.Count == 0)
            return $"все цели OK ({okCount}/{checks.Count}), средняя задержка {avgLatency:0} мс";

        return $"OK {okCount}/{checks.Count}, сбой: {string.Join(", ", failed)}";
    }
}
