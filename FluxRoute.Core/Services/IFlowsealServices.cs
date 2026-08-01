// ═══ v1.7.0: Интерфейсы Flowseal + Zapret2 ═══
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IFlowsealVersionManager
{
    Task<IReadOnlyList<FlowsealVersion>> GetAvailableVersionsAsync(CancellationToken ct = default);
    Task<FlowsealInstallResult> InstallVersionAsync(string version, string engineDir, CancellationToken ct = default);
    Task<FlowsealInstallResult> RollbackAsync(string engineDir, CancellationToken ct = default);
    string? GetCurrentVersion(string engineDir);
    Task<FlowsealVersion?> CheckForUpdateAsync(string engineDir, CancellationToken ct = default);
}

public interface IZapret2CompatibilityService
{
    Zapret2Capability CheckCapability();
    Task<Zapret2Capability> CheckCapabilityAsync(CancellationToken ct = default);
}
