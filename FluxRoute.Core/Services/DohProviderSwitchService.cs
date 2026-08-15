using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public interface IDohProviderSwitchService
{
    Task<DohApplyResult> SwitchAsync(
        string interfaceName,
        DohProvider previousProvider,
        DohEncryptionMode previousMode,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);

    Task<DohApplyResult> SwitchAsync(
        string interfaceName,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);
}

/// <summary>Переключает DoH-провайдера через транзакционное применение с автоматическим откатом.</summary>
public sealed class DohProviderSwitchService : IDohProviderSwitchService
{
    private readonly IWindowsDohConfigurationService _configurationService;

    public DohProviderSwitchService(IWindowsDohConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<DohApplyResult> SwitchAsync(
        string interfaceName,
        DohProvider previousProvider,
        DohEncryptionMode previousMode,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(provider);

        // ApplyAsync already captures the complete current state and restores it
        // transactionally on failure. An extra DHCP reset here creates a race
        // between Windows network reconfiguration and the next provider apply.
        _ = previousProvider;
        _ = previousMode;

        var applied = await _configurationService
            .ApplyAsync(interfaceName, provider, mode, ct)
            .ConfigureAwait(false);
        if (applied.IsSuccess)
        {
            return applied;
        }

        var outcome = applied.SwitchOutcome == DohProviderSwitchOutcome.RestoreFailed
            ? DohProviderSwitchOutcome.RestoreFailed
            : DohProviderSwitchOutcome.RestoredPrevious;
        return applied with
        {
            SwitchOutcome = outcome,
            Message = outcome == DohProviderSwitchOutcome.RestoredPrevious
                ? $"{applied.Message} Предыдущая конфигурация сохранена."
                : applied.Message
        };
    }

    public Task<DohApplyResult> SwitchAsync(
        string interfaceName,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default) =>
        SwitchAsync(interfaceName, provider, mode, provider, mode, ct);
}
