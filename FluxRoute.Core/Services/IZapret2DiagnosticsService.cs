using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IZapret2DiagnosticsService
{
    Task<Zapret2HealthInput> CollectAsync(
        string engineDirectory,
        string? profilePath,
        string? logPath,
        IReadOnlyList<TargetEntry> targets,
        bool userStopped,
        CancellationToken ct = default);
}