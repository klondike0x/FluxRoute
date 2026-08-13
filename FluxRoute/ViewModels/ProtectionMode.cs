namespace FluxRoute.ViewModels;

/// <summary>
/// Режим управления защитой на главном экране.
/// </summary>
public enum ProtectionMode
{
    Manual,
    Automatic
}

/// <summary>
/// Чистая политика отображения режима защиты.
/// </summary>
public static class ProtectionModePolicy
{
    public static ProtectionMode FromOrchestratorEnabled(bool enabled) =>
        enabled ? ProtectionMode.Automatic : ProtectionMode.Manual;

    public static string GetDisplayName(ProtectionMode mode) => mode switch
    {
        ProtectionMode.Automatic => "Автовыбор",
        ProtectionMode.Manual => "Вручную",
        _ => string.Empty
    };
}
