using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

/// <summary>
/// Менеджер модов: сканирование, активация, деактивация, проверка зависимостей.
/// </summary>
public interface IModManager
{
    /// <summary>Сканирует папку модов и возвращает список всех найденных модов с их статусами.</summary>
    Task<List<ModInfo>> ScanModsAsync(CancellationToken ct = default);

    /// <summary>Активирует мод: проверяет зависимости, запускает скрипт start.</summary>
    Task<bool> ActivateModAsync(string folderName, CancellationToken ct = default);

    /// <summary>Деактивирует мод: запускает скрипт stop.</summary>
    Task<bool> DeactivateModAsync(string folderName, CancellationToken ct = default);

    /// <summary>Возвращает текущий статус мода из кеша.</summary>
    ModStatus GetModStatus(string folderName);

    /// <summary>Проверяет, что все зависимости мода активны.</summary>
    Task<bool> CheckDependenciesAsync(string folderName, CancellationToken ct = default);

    /// <summary>Принудительно пересканирует моды (без запуска скриптов).</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>Создаёт новый мод с базовым manifest.json и start.bat.</summary>
    Task<ModInfo> CreateModAsync(string name, CancellationToken ct = default);

    /// <summary>Импортирует мод из папки (копирует содержимое в mods/).</summary>
    Task<ModInfo> ImportFromFolderAsync(string folderPath, CancellationToken ct = default);

    /// <summary>Импортирует мод из ZIP-архива (может содержать коллекцию модов).</summary>
    Task<List<ModInfo>> ImportFromZipAsync(string zipPath, CancellationToken ct = default);

    /// <summary>Импортирует мод из произвольных файлов (создаёт папку с этими файлами).</summary>
    Task<ModInfo> ImportFromFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default);

    /// <summary>Импортирует моды из GitHub-репозитория (скачивает zip и распаковывает; поддерживает коллекции).</summary>
    Task<List<ModInfo>> ImportFromGithubAsync(string repoUrl, CancellationToken ct = default);

    /// <summary>
    /// Синхронизирует моды из GitHub-репозитория: скачивает, находит все моды и
    /// устанавливает недостающие (уже установленные пропускаются).
    /// </summary>
    Task<ModSyncResult> SyncFromGithubAsync(string repoUrl, CancellationToken ct = default);

    /// <summary>
    /// Синхронно возвращает установленные моды (папки mods/ + status.json).
    /// Используется в LoadProfiles() для показа активных модов в главном списке.
    /// </summary>
    IReadOnlyList<ModInfo> GetInstalledMods();

    /// <summary>Возвращает путь к папке модов.</summary>
    string ModsPath { get; }

    /// <summary>Обновляет метаданные мода, сохраняя его скрипты и конфигурацию.</summary>
    Task<bool> UpdateModMetadataAsync(
        string folderName,
        string name,
        string version,
        string author,
        string description,
        CancellationToken ct = default);

    /// <summary>Экспортирует папку мода в ZIP-архив.</summary>
    Task ExportModAsync(string folderName, string destinationPath, CancellationToken ct = default);
}

/// <summary>
/// Результат синхронизации модов из GitHub-репозитория.
/// </summary>
public sealed record ModSyncResult(int Added, int Skipped, IReadOnlyList<string> AddedNames);
