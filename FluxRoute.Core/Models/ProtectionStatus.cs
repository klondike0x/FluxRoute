namespace FluxRoute.Core.Models;

/// <summary>Итоговое состояние основного компонента защиты.</summary>
public enum ProtectionStatus
{
    Stopped,
    Starting,
    Stopping,
    Healthy,
    Degraded,
    Error,
    Repairing
}

/// <summary>Состояние отдельной проверки, отображаемой на главном экране.</summary>
public enum ProtectionCheckState
{
    Unknown,
    Healthy,
    Degraded,
    Error,
    Stopped
}
