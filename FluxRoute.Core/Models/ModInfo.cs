namespace FluxRoute.Core.Models;

/// <summary>
/// Информация о моде для отображения в интерфейсе.
/// </summary>
public sealed class ModInfo
{
    /// <summary>Имя папки мода (уникальный идентификатор).</summary>
    public string FolderName { get; set; } = string.Empty;

    /// <summary>Название мода из манифеста.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Версия.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Автор.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Описание.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Текущий статус.</summary>
    public ModStatus Status { get; set; } = ModStatus.NotLoaded;

    /// <summary>Сообщение об ошибке (если Status == Error).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Зависимости — имена папок других модов.</summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Активен ли мод (для привязки iOS-переключателя).</summary>
    public bool IsActive => Status == ModStatus.Active;

    /// <summary>Есть ли скрипт start.</summary>
    public bool HasStartScript { get; set; }

    /// <summary>Есть ли скрипт stop.</summary>
    public bool HasStopScript { get; set; }
}
