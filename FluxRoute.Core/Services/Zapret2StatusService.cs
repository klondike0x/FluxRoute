using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public sealed class Zapret2StatusService : IZapret2StatusService
{
    private readonly IZapret2ProcessHost _processHost;
    private readonly ILogger<Zapret2StatusService> _logger;
    private Zapret2StatusSnapshot _current = Zapret2StatusSnapshot.Stopped();

    public Zapret2StatusService(
        IZapret2ProcessHost processHost,
        ILogger<Zapret2StatusService> logger)
    {
        _processHost = processHost;
        _logger = logger;
    }

    public Zapret2StatusSnapshot Current => _current;

    public void PublishOperationStatus(ProtectionStatus status, string diagnosticMessage)
    {
        _current = _current with
        {
            Status = status,
            DiagnosticMessage = diagnosticMessage,
            CheckedAt = DateTimeOffset.Now
        };
    }

    public Task<Zapret2StatusSnapshot> CheckAsync(
        Zapret2HealthInput input,
        string activeProfile,
        string? logPath = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var status = ProtectionStatusEvaluator.Evaluate(input);
        var checks = CreateChecks(input);
        var diagnostic = BuildDiagnostic(status, input, logPath);

        _current = new Zapret2StatusSnapshot
        {
            Status = status,
            Winws2Running = input.Winws2Running,
            WinDivertAvailable = input.WinDivertAvailable,
            ProfileLoaded = input.ProfileLoaded,
            StrategyActive = input.StrategyActive,
            ConnectionHealthy = input.ConnectionHealthy,
            CriticalLogErrors = input.CriticalLogErrors,
            TrafficMonitoringAvailable = input.TrafficMonitoringAvailable,
            ActiveProfile = string.IsNullOrWhiteSpace(activeProfile) ? "—" : activeProfile,
            DiagnosticMessage = diagnostic,
            CheckedAt = DateTimeOffset.Now,
            Checks = checks
        };

        return Task.FromResult(_current);
    }

    public async Task<Zapret2OperationResult> StartAsync(
        Zapret2StartRequest request,
        CancellationToken ct = default)
    {
        _current = _current with
        {
            Status = ProtectionStatus.Starting,
            DiagnosticMessage = "Запуск winws2…",
            CheckedAt = DateTimeOffset.Now
        };

        var result = await _processHost.StartAsync(request, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            _current = _current with
            {
                Status = ProtectionStatus.Error,
                DiagnosticMessage = result.Message,
                CheckedAt = DateTimeOffset.Now
            };
        }

        return result;
    }

    public async Task<Zapret2OperationResult> StopAsync(CancellationToken ct = default)
    {
        _current = _current with
        {
            Status = ProtectionStatus.Stopping,
            DiagnosticMessage = "Остановка winws2…",
            CheckedAt = DateTimeOffset.Now
        };

        var result = await _processHost.StopAsync(ct).ConfigureAwait(false);
        if (result.Success)
            _current = Zapret2StatusSnapshot.Stopped(result.Message);

        return result;
    }

    public async Task<Zapret2OperationResult> RepairAsync(
        Zapret2RepairRequest request,
        Func<CancellationToken, Task<Zapret2StatusSnapshot>> checkAfterRestart,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(checkAfterRestart);

        _current = _current with
        {
            Status = ProtectionStatus.Repairing,
            DiagnosticMessage = "Выполняется автоматическое восстановление…",
            CheckedAt = DateTimeOffset.Now
        };

        var stop = await _processHost.StopAsync(ct).ConfigureAwait(false);
        if (!stop.Success)
            return new(false, $"Восстановление остановлено: {stop.Message}");

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

        var message = start.Success
            ? "Проверка после восстановления не пройдена."
            : start.Message;
        _current = _current with
        {
            Status = ProtectionStatus.Error,
            DiagnosticMessage = message,
            CheckedAt = DateTimeOffset.Now
        };
        return new(false, message);
    }

    private static IReadOnlyList<Zapret2ComponentCheck> CreateChecks(Zapret2HealthInput input)
    {
        var engineState = input.Winws2Running
            ? ProtectionCheckState.Healthy
            : input.UserStopped ? ProtectionCheckState.Stopped : ProtectionCheckState.Error;

        return [
            new("winws2", "winws2", engineState,
                input.Winws2Running ? "Процесс запущен." : input.UserStopped ? "Процесс остановлен пользователем." : "Процесс не найден."),
            new("windivert", "WinDivert", ToState(input.WinDivertAvailable),
                input.WinDivertAvailable ? "Драйвер и библиотека доступны." : "WinDivert недоступен."),
            new("profile", "Активный профиль", ToState(input.ProfileLoaded && input.StrategyActive),
                input.ProfileLoaded && input.StrategyActive ? "Профиль загружен и активен." : "Профиль не загружен или не активен."),
            new("connection", "Соединение", ToState(input.ConnectionHealthy),
                input.ConnectionHealthy ? "Проверка соединения пройдена." : "Проверка соединения не пройдена."),
            new("logs", "Критические ошибки", input.CriticalLogErrors ? ProtectionCheckState.Error : ProtectionCheckState.Healthy,
                input.CriticalLogErrors ? "В логах найдена критическая ошибка." : "Критические ошибки не найдены."),
            new("traffic", "Мониторинг трафика", ToState(input.TrafficMonitoringAvailable),
                input.TrafficMonitoringAvailable ? "Мониторинг доступен." : "Мониторинг недоступен.")
        ];
    }

    private static ProtectionCheckState ToState(bool value) =>
        value ? ProtectionCheckState.Healthy : ProtectionCheckState.Error;

    private static string BuildDiagnostic(ProtectionStatus status, Zapret2HealthInput input, string? logPath) => status switch
    {
        ProtectionStatus.Stopped => "Пользователь остановил защиту.",
        ProtectionStatus.Starting => "Запуск winws2…",
        ProtectionStatus.Stopping => "Остановка winws2…",
        ProtectionStatus.Repairing => "Выполняется автоматическое восстановление…",
        ProtectionStatus.Healthy => "Все проверки Zapret2 пройдены.",
        ProtectionStatus.Degraded => "winws2 работает, но часть проверок не пройдена.",
        _ when !input.WinDivertAvailable => "WinDivert недоступен.",
        _ when !input.Winws2Running => "winws2 не запущен или завершился.",
        _ when input.CriticalLogErrors => $"Критическая ошибка в логах{(string.IsNullOrWhiteSpace(logPath) ? "." : $": {logPath}")}",
        _ => "Критическая проверка Zapret2 не пройдена."
    };
}

public static class ProtectionStatusEvaluator
{
    public static ProtectionStatus Evaluate(Zapret2HealthInput input)
    {
        if (input.IsRepairing)
            return ProtectionStatus.Repairing;
        if (input.IsStarting)
            return ProtectionStatus.Starting;
        if (input.UserStopped)
            return ProtectionStatus.Stopped;
        if (!input.Winws2Running || !input.WinDivertAvailable || !input.ProfileLoaded || !input.StrategyActive || input.CriticalLogErrors)
            return ProtectionStatus.Error;
        if (!input.ConnectionHealthy || !input.TrafficMonitoringAvailable)
            return ProtectionStatus.Degraded;
        return ProtectionStatus.Healthy;
    }
}