namespace FluxRoute.Core.Services;

public interface IDohStartupRecovery
{
    Task RecoverAsync(CancellationToken ct = default);
}

/// <summary>Восстанавливает незавершённую DoH-транзакцию до создания главного окна.</summary>
public sealed class DohStartupRecovery : IDohStartupRecovery
{
    private readonly IWindowsDohConfigurationService _configurationService;

    public DohStartupRecovery(IWindowsDohConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        var result = await _configurationService.RecoverPendingOperationAsync(ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Message);
        }
    }
}
