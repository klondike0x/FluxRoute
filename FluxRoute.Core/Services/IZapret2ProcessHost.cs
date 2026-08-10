using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed record Zapret2ProcessSnapshot(
    bool IsRunning,
    IReadOnlyList<int> ProcessIds,
    string Detail);

public interface IZapret2ProcessHost
{
    Zapret2ProcessSnapshot Snapshot();
    Task<Zapret2OperationResult> StartAsync(Zapret2StartRequest request, CancellationToken ct = default);
    Task<Zapret2OperationResult> StopAsync(CancellationToken ct = default);
}