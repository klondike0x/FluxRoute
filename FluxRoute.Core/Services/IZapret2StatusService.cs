using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IZapret2StatusService
{
    Zapret2StatusSnapshot Current { get; }

    void PublishOperationStatus(ProtectionStatus status, string diagnosticMessage);

    Task<Zapret2StatusSnapshot> CheckAsync(
        Zapret2HealthInput input,
        string activeProfile,
        string? logPath = null,
        CancellationToken ct = default);

    Task<Zapret2OperationResult> StartAsync(
        Zapret2StartRequest request,
        CancellationToken ct = default);

    Task<Zapret2OperationResult> StopAsync(CancellationToken ct = default);

}
