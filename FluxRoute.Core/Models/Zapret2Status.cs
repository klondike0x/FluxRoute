using System.Collections.ObjectModel;

namespace FluxRoute.Core.Models;

public sealed record Zapret2ComponentCheck(
    string Key,
    string Title,
    ProtectionCheckState State,
    string Detail);

public sealed record Zapret2HealthInput(
    bool UserStopped,
    bool IsStarting,
    bool IsRepairing,
    bool Winws2Running,
    bool WinDivertAvailable,
    bool ProfileLoaded,
    bool StrategyActive,
    bool ConnectionHealthy,
    bool CriticalLogErrors,
    bool TrafficMonitoringAvailable);

public sealed record Zapret2RuntimeRequest(
    string EngineDirectory,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string? ProfilePath,
    string? LogPath,
    IReadOnlyList<TargetEntry> Targets,
    bool UserStopped = false,
    bool IsStarting = false,
    bool IsRepairing = false,
    int MaxParallelChecks = 6);

public sealed record Zapret2StatusSnapshot
{
    public ProtectionStatus Status { get; init; } = ProtectionStatus.Stopped;
    public bool Winws2Running { get; init; }
    public bool WinDivertAvailable { get; init; }
    public bool ProfileLoaded { get; init; }
    public bool StrategyActive { get; init; }
    public bool ConnectionHealthy { get; init; }
    public bool CriticalLogErrors { get; init; }
    public bool TrafficMonitoringAvailable { get; init; }
    public string ActiveProfile { get; init; } = "—";
    public string DiagnosticMessage { get; init; } = "Пользователь остановил защиту.";
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<Zapret2ComponentCheck> Checks { get; init; } =
        Array.Empty<Zapret2ComponentCheck>();

    public Zapret2ComponentCheck GetCheck(string key) =>
        Checks.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? new Zapret2ComponentCheck(key, key, ProtectionCheckState.Unknown, "Проверка не выполнялась.");

    public static Zapret2StatusSnapshot Stopped(string message = "Пользователь остановил защиту.") =>
        new()
        {
            Status = ProtectionStatus.Stopped,
            DiagnosticMessage = message,
            CheckedAt = DateTimeOffset.Now,
            Checks = new ReadOnlyCollection<Zapret2ComponentCheck>([
                new("winws2", "winws2", ProtectionCheckState.Stopped, "Процесс остановлен."),
                new("windivert", "WinDivert", ProtectionCheckState.Unknown, "Не проверяется в остановленном состоянии."),
                new("profile", "Профиль", ProtectionCheckState.Unknown, "Не проверяется в остановленном состоянии."),
                new("connection", "Соединение", ProtectionCheckState.Unknown, "Не проверяется в остановленном состоянии."),
                new("logs", "Логи", ProtectionCheckState.Unknown, "Не проверяется в остановленном состоянии."),
                new("traffic", "Трафик", ProtectionCheckState.Stopped, "Мониторинг остановлен.")
            ])
        };
}

public sealed record Zapret2StartRequest(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string ActiveProfile,
    string? LogPath = null);

public sealed record Zapret2RepairRequest(
    Zapret2StartRequest ActiveProfile,
    Zapret2StartRequest? FallbackProfile = null,
    bool AllowFallback = false,
    TimeSpan RestartDelay = default);

public sealed record Zapret2OperationResult(
    bool Success,
    string Message,
    int? ProcessId = null);