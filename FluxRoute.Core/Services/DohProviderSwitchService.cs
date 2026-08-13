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

/// <summary>Атомарно переключает DoH-провайдера через промежуточный возврат DNS к DHCP.</summary>
public sealed class DohProviderSwitchService : IDohProviderSwitchService
{
    private readonly IWindowsDohConfigurationService _configurationService;

    public DohProviderSwitchService(IWindowsDohConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<DohApplyResult> SwitchAsync(
        string interfaceName,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        return await SwitchAsync(interfaceName, provider, mode, provider, mode, ct).ConfigureAwait(false);
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

        var resetResult = await _configurationService
            .DisableAsync(interfaceName, ct)
            .ConfigureAwait(false);
        if (!resetResult.IsSuccess)
        {
            return new DohApplyResult(
                false,
                $"Не удалось вернуть DNS к DHCP перед переключением: {resetResult.Message}",
                DohProviderSwitchOutcome.RestoreFailed);
        }

        var applied = await _configurationService
            .ApplyAsync(interfaceName, provider, mode, ct)
            .ConfigureAwait(false);
        if (applied.IsSuccess)
        {
            return applied;
        }

        var restored = await _configurationService
            .ApplyAsync(interfaceName, previousProvider, previousMode, CancellationToken.None)
            .ConfigureAwait(false);
        return restored.IsSuccess
            ? new(false, $"{applied.Message} Предыдущий провайдер восстановлен.", DohProviderSwitchOutcome.RestoredPrevious)
            : new(false, $"{applied.Message} Не удалось восстановить предыдущий провайдер: {restored.Message}", DohProviderSwitchOutcome.RestoreFailed);
    }
}
