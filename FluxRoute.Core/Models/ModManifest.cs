using System.Text.Json.Serialization;

namespace FluxRoute.Core.Models;

/// <summary>
/// Манифест мода — десериализуется из mods/&lt;mod-name&gt;/manifest.json.
/// </summary>
public sealed class ModManifest
{
    /// <summary>Название мода.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Версия (SemVer).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Автор.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>Описание.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Зависимости — имена других модов, которые должны быть активны.</summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Скрипты запуска/остановки.</summary>
    [JsonPropertyName("scripts")]
    public ModScripts? Scripts { get; set; }

    /// <summary>Пользовательская конфигурация (свободная форма).</summary>
    [JsonPropertyName("config")]
    public Dictionary<string, object>? Config { get; set; }
}

/// <summary>
/// Скрипты мода.
/// </summary>
public sealed class ModScripts
{
    /// <summary>Команда для запуска мода.</summary>
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    /// <summary>Команда для остановки мода.</summary>
    [JsonPropertyName("stop")]
    public string? Stop { get; set; }
}
