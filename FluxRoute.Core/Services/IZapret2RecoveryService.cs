using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IZapret2RecoveryService
{
    Task<Zapret2OperationResult> RecoverAsync(
        Zapret2RepairRequest request,
        Func<CancellationToken, Task<Zapret2StatusSnapshot>> checkAfterRestart,
        CancellationToken ct = default);
}