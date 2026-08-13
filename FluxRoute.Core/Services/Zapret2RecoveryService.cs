using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public sealed class Zapret2RecoveryService : IZapret2RecoveryService
{
    private readonly IZapret2ProcessHost _processHost;
    private readonly IZapret2StatusService _statusService;
    private readonly ILogger<Zapret2RecoveryService> _logger;

    public Zapret2RecoveryService(
        IZapret2ProcessHost processHost,
        IZapret2StatusService statusService,
        ILogger<Zapret2RecoveryService> logger)
    {
        _processHost = processHost;
        _statusService = statusService;
        _logger = logger;
    }

    public async Task<Zapret2OperationResult> RecoverAsync(
        Zapret2RepairRequest request,
        Func<CancellationToken, Task<Zapret2StatusSnapshot>> checkAfterRestart,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(checkAfterRestart);

        _statusService.PublishOperationStatus(
            ProtectionStatus.Repairing,
            "Выполняется автоматическое восстановление…");

        var stop = await _processHost.StopAsync(ct).ConfigureAwait(false);
        if (!stop.Success)
        {
            return Fail($"Восстановление остановлено: {stop.Message}");
        }

        var delay = request.RestartDelay == default
            ? TimeSpan.FromMilliseconds(250)
            : request.RestartDelay;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct).ConfigureAwait(false);

        var start = await _processHost.StartAsync(request.ActiveProfile, ct).ConfigureAwait(false);
        if (start.Success)
        {
            var snapshot = await checkAfterRestart(ct).ConfigureAwait(false);
            if (snapshot.Status is ProtectionStatus.Healthy or ProtectionStatus.Degraded)
                return new(true, "Zapret2 восстановлен.", start.ProcessId);
        }

        if (request.AllowFallback && request.FallbackProfile is not null)
        {
            _logger.LogWarning("Активный профиль Zapret2 не восстановился. Используется разрешённый fallback.");
            await _processHost.StopAsync(ct).ConfigureAwait(false);
            var fallback = await _processHost.StartAsync(request.FallbackProfile, ct).ConfigureAwait(false);
            if (fallback.Success)
            {
                var snapshot = await checkAfterRestart(ct).ConfigureAwait(false);
                if (snapshot.Status is ProtectionStatus.Healthy or ProtectionStatus.Degraded)
                    return new(true, "Zapret2 восстановлен с резервным профилем.", fallback.ProcessId);
            }
        }

        return Fail(start.Success
            ? "Проверка после восстановления не пройдена."
            : start.Message);
    }

    private Zapret2OperationResult Fail(string message)
    {
        _statusService.PublishOperationStatus(ProtectionStatus.Error, message);
        return new(false, message);
    }
}