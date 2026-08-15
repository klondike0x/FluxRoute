namespace FluxRoute.Core.Models;

/// <summary>
/// Статус мода.
/// </summary>
public enum ModStatus
{
    /// <summary>Мод загружен, но не запущен.</summary>
    Inactive,
    /// <summary>Мод активен (скрипт start выполнен успешно).</summary>
    Active,
    /// <summary>Ошибка при запуске/остановке или нарушена структура.</summary>
    Error,
    /// <summary>Мод обнаружен, но ещё не загружался.</summary>
    NotLoaded
}
