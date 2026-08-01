// ═══ v1.7.0: IEngineSwitchService ═══
namespace FluxRoute.Core.Services;

public interface IEngineSwitchService
{
    Task<bool> CanSwitchToAsync(string engineType, CancellationToken ct = default);
    Task<bool> TestStrategyAsync(string profilePath, IReadOnlyList<string> targetSites, CancellationToken ct = default);
}
