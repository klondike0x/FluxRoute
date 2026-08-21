using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public sealed class Zapret2DiagnosticsService : IZapret2DiagnosticsService
{
    private readonly IZapret2ProcessHost _processHost;
    private readonly IConnectivityChecker _connectivity;
    private readonly INetworkTrafficMonitor? _trafficMonitor;
    private readonly ILogger<Zapret2DiagnosticsService> _logger;

    public Zapret2DiagnosticsService(
        IZapret2ProcessHost processHost,
        IConnectivityChecker connectivity,
        INetworkTrafficMonitor? trafficMonitor,
        ILogger<Zapret2DiagnosticsService> logger)
    {
        _processHost = processHost;
        _connectivity = connectivity;
        _trafficMonitor = trafficMonitor;
        _logger = logger;
    }

    public async Task<Zapret2HealthInput> CollectAsync(
        string engineDirectory,
        string? profilePath,
        string? logPath,
        IReadOnlyList<TargetEntry> targets,
        bool userStopped,
        CancellationToken ct = default)
    {
        var process = _processHost.Snapshot();
        var connectionHealthy = false;

        if (targets.Count > 0)
        {
            try
            {
                var result = await _connectivity.CheckAllAsync(
                    targets,
                    useCurlForHttp: true,
                    maxParallelChecks: 6,
                    ct).ConfigureAwait(false);
                connectionHealthy = result.successRate >= 0.999;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проверка соединения Zapret2 завершилась ошибкой.");
            }
        }

        return new Zapret2HealthInput(
            UserStopped: userStopped,
            IsStarting: false,
            IsRepairing: false,
            Winws2Running: process.IsRunning,
            WinDivertAvailable: HasWinDivert(engineDirectory),
            ProfileLoaded: !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath),
            StrategyActive: process.IsRunning && !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath),
            ConnectionHealthy: connectionHealthy,
            CriticalLogErrors: HasCriticalLogErrors(logPath),
            TrafficMonitoringAvailable: _trafficMonitor?.Sample().IsAvailable ?? false);
    }

    private static bool HasWinDivert(string engineDirectory)
    {
        var bin = Path.Combine(engineDirectory, "bin");
        return (File.Exists(Path.Combine(bin, "WinDivert.dll"))
            || File.Exists(Path.Combine(bin, "WinDivert64.dll")))
            && (File.Exists(Path.Combine(bin, "WinDivert64.sys"))
            || File.Exists(Path.Combine(bin, "WinDivert.sys")));
    }

    private static bool HasCriticalLogErrors(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return false;

        try
        {
            return File.ReadLines(logPath).TakeLast(250).Any(line =>
                line.Contains("fatal", StringComparison.OrdinalIgnoreCase)
                || line.Contains("critical", StringComparison.OrdinalIgnoreCase)
                || line.Contains("cannot load", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed to initialize", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}