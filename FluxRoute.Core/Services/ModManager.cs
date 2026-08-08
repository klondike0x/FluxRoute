using System.Diagnostics;
using System.Text.Json;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public sealed class ModManager : IModManager, IDisposable
{
    private readonly string _modsPath;
    private readonly string? _engineDir;
    private readonly string _statusFilePath;
    private readonly ILogger<ModManager> _logger;
    private readonly Dictionary<string, ModStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModInfo> _modCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _statusLock = new(1, 1);

    public ModManager(string modsPath, ILogger<ModManager> logger, string? engineDir = null)
    {
        _modsPath = modsPath ?? throw new ArgumentNullException(nameof(modsPath));
        _engineDir = engineDir;
        _statusFilePath = Path.Combine(_modsPath, "status.json");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ModInfo>> ScanModsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("ScanMods: {ModsPath}", _modsPath);

        await _statusLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LoadStatuses();
            _modCache.Clear();

            if (!Directory.Exists(_modsPath))
            {
                _logger.LogWarning("Mods folder not found: {ModsPath}", _modsPath);
                Directory.CreateDirectory(_modsPath);
                return new List<ModInfo>();
            }

            var result = new List<ModInfo>();

            foreach (var dir in Directory.GetDirectories(_modsPath))
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(dir);
                var manifestPath = Path.Combine(dir, "manifest.json");

                if (!File.Exists(manifestPath))
                {
                    _logger.LogDebug("Skipping folder without manifest.json: {Folder}", folderName);
                    continue;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<ModManifest>(json);

                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Name))
                    {
                        _logger.LogWarning("Invalid manifest.json in folder {Folder}", folderName);
                        continue;
                    }

                    var status = _statusCache.TryGetValue(folderName, out var cached)
                        ? cached
                        : ModStatus.Inactive;

                    var modInfo = new ModInfo
                    {
                        FolderName = folderName,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        Author = manifest.Author,
                        Description = manifest.Description,
                        Status = status,
                        Dependencies = manifest.Dependencies ?? new List<string>(),
                        HasStartScript = !string.IsNullOrWhiteSpace(manifest.Scripts?.Start),
                        HasStopScript = !string.IsNullOrWhiteSpace(manifest.Scripts?.Stop)
                    };

                    _modCache[folderName] = modInfo;
                    result.Add(modInfo);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "JSON parse error in {Folder}", folderName);
                    result.Add(new ModInfo
                    {
                        FolderName = folderName,
                        Name = folderName,
                        Status = ModStatus.Error,
                        ErrorMessage = $"JSON: {ex.Message}"
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error scanning mod {Folder}", folderName);
                    result.Add(new ModInfo
                    {
                        FolderName = folderName,
                        Name = folderName,
                        Status = ModStatus.Error,
                        ErrorMessage = ex.Message
                    });
                }
            }

            _logger.LogInformation("Scan complete: {Count} mods", result.Count);
            return result;
        }
        finally
        {
            _statusLock.Release();
        }
    }

    public async Task<bool> ActivateModAsync(string folderName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        _logger.LogInformation("Activating mod: {Folder}", folderName);

        if (!await CheckDependenciesInternalAsync(folderName, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Dependencies not satisfied for '{folderName}'.");

        var manifest = await LoadManifestAsync(folderName, ct).ConfigureAwait(false);
        if (manifest == null)
        {
            _logger.LogWarning("Mod {Folder} not found", folderName);
            return false;
        }

        // Моды без start-скрипта (например, импортированные из Zapret-Hub — чистые списки/файлы)
        // активируются простым переключением статуса
        if (manifest.Scripts?.Start == null)
        {
            _logger.LogInformation("Mod {Folder} activated without start script (files/lists)", folderName);
            await SetStatusAsync(folderName, ModStatus.Active).ConfigureAwait(false);
            await CopyModBatsToEngineForFolder(folderName, ct).ConfigureAwait(false);
            return true;
        }

        var modDir = Path.Combine(_modsPath, folderName);
        var success = await RunScriptAsync(modDir, manifest.Scripts.Start, "start", ct).ConfigureAwait(false);

        // Всегда активируем + копируем .bat в engine/
        await SetStatusAsync(folderName,
            success ? ModStatus.Active : ModStatus.Active,
            success ? null : "Start script returned non-zero exit code (activated anyway)").ConfigureAwait(false);
        await CopyModBatsToEngineForFolder(folderName, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeactivateModAsync(string folderName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        _logger.LogInformation("Deactivating mod: {Folder}", folderName);

        var manifest = await LoadManifestAsync(folderName, ct).ConfigureAwait(false);
        if (manifest?.Scripts?.Stop == null)
        {
            await SetStatusAsync(folderName, ModStatus.Inactive).ConfigureAwait(false);
            RemoveModBatsFromEngine(Path.Combine(_modsPath, folderName), _engineDir!);
            return true;
        }

        var modDir = Path.Combine(_modsPath, folderName);
        var success = await RunScriptAsync(modDir, manifest.Scripts.Stop, "stop", ct).ConfigureAwait(false);

        if (success)
            await SetStatusAsync(folderName, ModStatus.Inactive).ConfigureAwait(false);
        else
            await SetStatusAsync(folderName, ModStatus.Error, "Stop script returned non-zero exit code").ConfigureAwait(false);

        // Убираем .bat мода из engine/ — он пропадёт из списка профилей
        RemoveModBatsFromEngine(modDir, _engineDir!);
        return success;
    }

    public ModStatus GetModStatus(string folderName)
    {
        if (_statusCache.TryGetValue(folderName, out var status)) return status;
        if (_modCache.TryGetValue(folderName, out var mod)) return mod.Status;
        return ModStatus.NotLoaded;
    }

    public Task<bool> CheckDependenciesAsync(string folderName, CancellationToken ct = default)
        => CheckDependenciesInternalAsync(folderName, ct);

    public async Task RefreshAsync(CancellationToken ct = default)
        => await ScanModsAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public string ModsPath => _modsPath;

    /// <inheritdoc />
    public async Task<ModInfo> CreateModAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Нормализуем имя: латиница, цифры, дефис — безопасное имя папки
        var folderName = NormalizeFolderName(name);
        if (folderName.Length == 0)
            throw new InvalidOperationException("Имя мода должно содержать латинские буквы или цифры.");

        var modDir = Path.Combine(_modsPath, folderName);
        if (Directory.Exists(modDir))
            throw new InvalidOperationException($"Мод «{folderName}» уже существует.");

        Directory.CreateDirectory(modDir);
        ct.ThrowIfCancellationRequested();

        var manifest = new ModManifest
        {
            Name = name.Trim(),
            Version = "1.0.0",
            Author = "FluxRoute",
            Description = "Новый мод",
            Dependencies = new List<string>(),
            Scripts = new ModScripts { Start = "start.bat", Stop = "stop.bat" },
            Config = new Dictionary<string, object>()
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(modDir, "manifest.json"), json, ct).ConfigureAwait(false);

        var startBat = "@echo off\r\necho Mod start\r\nexit /b 0\r\n";
        var stopBat = "@echo off\r\necho Mod stop\r\nexit /b 0\r\n";
        await File.WriteAllTextAsync(Path.Combine(modDir, "start.bat"), startBat, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(modDir, "stop.bat"), stopBat, ct).ConfigureAwait(false);

        _logger.LogInformation("Created mod {Folder} ({Name})", folderName, name);

        await ScanModsAsync(ct).ConfigureAwait(false);
        return _modCache.TryGetValue(folderName, out var mod)
            ? mod
            : new ModInfo { FolderName = folderName, Name = name, Status = ModStatus.Inactive };
    }

    /// <inheritdoc />
    public IReadOnlyList<ModInfo> GetInstalledMods()
    {
        var result = new List<ModInfo>();

        try
        {
            LoadStatuses();
            if (!Directory.Exists(_modsPath))
                return result;

            foreach (var dir in Directory.GetDirectories(_modsPath))
            {
                var folderName = Path.GetFileName(dir);
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(manifestPath));
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Name))
                        continue;

                    var status = _statusCache.TryGetValue(folderName, out var cached)
                        ? cached
                        : ModStatus.Inactive;

                    result.Add(new ModInfo
                    {
                        FolderName = folderName,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        Author = manifest.Author,
                        Description = manifest.Description,
                        Status = status,
                        Dependencies = manifest.Dependencies ?? new List<string>(),
                        HasStartScript = !string.IsNullOrWhiteSpace(manifest.Scripts?.Start),
                        HasStopScript = !string.IsNullOrWhiteSpace(manifest.Scripts?.Stop)
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "JSON parse error in {Folder}", folderName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInstalledMods failed");
        }

        return result;
    }

    /// <summary>
    /// Копирует .bat мода в engine/ по имени папки.
    /// </summary>
    private async Task CopyModBatsToEngineForFolder(string folderName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_engineDir))
            return;
        var modDir = Path.Combine(_modsPath, folderName);
        if (Directory.Exists(modDir))
            await CopyModBatsToEngineAsync(modDir, _engineDir, modDir, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Копирует файлы мода (оригинальные .bat, .bin, списки) в engine/,
    /// чтобы они появились в главном ComboBox как обычные стратегии.
    /// </summary>
    private async Task CopyModBatsToEngineAsync(string modDir, string engineDir, string sourceDir, CancellationToken ct)
    {
        try
        {
            // Копируем только .bat файлы из папки мода в engine/
            foreach (var file in Directory.GetFiles(modDir, "*.bat", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                // Пропускаем наши служебные .bat
                if (name is "start.bat" or "stop.bat")
                    continue;

                var relPath = Path.GetRelativePath(modDir, file);
                var dest = Path.Combine(engineDir, relPath);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                await Task.Run(() => File.Copy(file, dest, overwrite: true), ct).ConfigureAwait(false);

                // Исправляем winws.exe → %BIN%winws.exe (как в штатных стратегиях)
                var content = await File.ReadAllTextAsync(dest, ct).ConfigureAwait(false);
                if (!content.Contains("%BIN%winws.exe", StringComparison.OrdinalIgnoreCase) &&
                    content.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace("winws.exe", "%BIN%winws.exe");
                    await File.WriteAllTextAsync(dest, content, ct).ConfigureAwait(false);
                }
            }
            _logger.LogInformation("Скопированы файлы мода в engine/: {Dir}", modDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка копирования файлов мода в engine/");
        }
    }

    /// <summary>
    /// Удаляет .bat файлы мода из engine/ (при выключении/удалении).
    /// </summary>
    private void RemoveModBatsFromEngine(string modDir, string engineDir)
    {
        if (string.IsNullOrWhiteSpace(engineDir) || !Directory.Exists(engineDir))
            return;

        try
        {
            foreach (var bat in Directory.GetFiles(modDir, "*.bat", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(bat);
                if (string.Equals(name, "start.bat", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "stop.bat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var engineBat = Path.Combine(engineDir, name);
                if (File.Exists(engineBat))
                {
                    File.Delete(engineBat);
                    _logger.LogInformation("Удалён .bat из engine/: {File}", name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка удаления .bat из engine/");
        }
    }

    /// <summary>
    /// Приводит имя мода к безопасному имени папки: латиница, цифры, дефис, нижний регистр.
    /// </summary>
    private static string NormalizeFolderName(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch == '-')
                builder.Append(ch);
            else if (ch is ' ' or '_')
                builder.Append('-');
        }
        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<ModInfo> ImportFromFolderAsync(string folderPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Папка не найдена: {folderPath}");

        var folderName = NormalizeFolderName(Path.GetFileName(folderPath));
        if (folderName.Length == 0)
            folderName = $"mod-{DateTime.Now:yyyyMMddHHmmss}";

        var targetDir = Path.Combine(_modsPath, folderName);
        if (Directory.Exists(targetDir))
            targetDir = Path.Combine(_modsPath, $"{folderName}-{DateTime.Now:HHmmss}");

        Directory.CreateDirectory(targetDir);
        await CopyDirectoryAsync(folderPath, targetDir, ct).ConfigureAwait(false);

        // Если manifest.json отсутствует — конвертируем zapret-hub-mod.json или создаём минимальный
        var manifestPath = Path.Combine(targetDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            var hubManifest = Path.Combine(targetDir, "zapret-hub-mod.json");
            if (File.Exists(hubManifest))
            {
                await ConvertHubManifestAsync(targetDir, hubManifest, ct).ConfigureAwait(false);
            }
            else
            {
                var manifest = new ModManifest
                {
                    Name = Path.GetFileName(folderPath),
                    Version = "1.0.0",
                    Author = "FluxRoute",
                    Description = "Импортировано из папки",
                    Dependencies = new List<string>(),
                    Scripts = new ModScripts { Start = "start.bat", Stop = "stop.bat" },
                    Config = new Dictionary<string, object>()
                };
                await File.WriteAllTextAsync(manifestPath,
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                    ct).ConfigureAwait(false);

                // Генерируем start.bat/stop.bat (для папок без скриптов)
                await EnsureModScriptsAsync(targetDir, manifest, ct).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Импортирован мод из папки: {Folder}", targetDir);
        await ScanModsAsync(ct).ConfigureAwait(false);
        return _modCache.TryGetValue(Path.GetFileName(targetDir), out var mod)
            ? mod
            : new ModInfo { FolderName = Path.GetFileName(targetDir), Name = Path.GetFileName(folderPath), Status = ModStatus.Inactive };
    }

    /// <inheritdoc />
    public async Task<List<ModInfo>> ImportFromZipAsync(string zipPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Архив не найден: {zipPath}");

        // Распаковываем во временную папку, чтобы понять структуру (один мод или коллекция)
        var tempRoot = Path.Combine(Path.GetTempPath(), $"FluxRouteZip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempRoot), ct).ConfigureAwait(false);
            return await ImportFromExtractedRootAsync(tempRoot, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    /// <inheritdoc />
    public async Task<List<ModInfo>> ImportFromGithubAsync(string repoUrl, CancellationToken ct = default)
    {
        var (owner, repo) = ParseGithubUrl(repoUrl);
        _logger.LogInformation("Импорт модов из GitHub: {Owner}/{Repo}", owner, repo);

        var zipPath = await DownloadGithubZipAsync(owner, repo, ct).ConfigureAwait(false);
        try
        {
            return await ImportFromZipAsync(zipPath, ct).ConfigureAwait(false);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    /// <inheritdoc />
    public async Task<ModSyncResult> SyncFromGithubAsync(string repoUrl, CancellationToken ct = default)
    {
        var (owner, repo) = ParseGithubUrl(repoUrl);
        _logger.LogInformation("Синхронизация модов из GitHub: {Owner}/{Repo}", owner, repo);

        var zipPath = await DownloadGithubZipAsync(owner, repo, ct).ConfigureAwait(false);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"FluxRouteSync_{Guid.NewGuid():N}");
        var added = new List<string>();
        var skipped = 0;

        try
        {
            Directory.CreateDirectory(tempRoot);
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempRoot), ct).ConfigureAwait(false);

            // Определяем корень (архив может иметь вложенную папку owner-repo-commit)
            var root = tempRoot;
            var topDirs = Directory.GetDirectories(root);
            var topFiles = Directory.GetFiles(root);
            if (topFiles.Length == 0 && topDirs.Length == 1)
                root = topDirs[0];

            // Ищем папки-моды: с manifest.json или zapret-hub-mod.json
            var candidates = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name is "manifest.json" or "zapret-hub-mod.json")
                {
                    var dir = Path.GetDirectoryName(file)!;
                    var rel = Path.GetRelativePath(root, dir);
                    if (rel.Split(Path.DirectorySeparatorChar).Length <= 2)
                        candidates.Add(dir);
                }
            }

            // Дедупликация вложенных кандидатов (родительские папки)
            candidates = candidates
                .OrderBy(c => Path.GetRelativePath(root, c).Split(Path.DirectorySeparatorChar).Length)
                .Distinct()
                .ToList();

            foreach (var candidateDir in candidates)
            {
                ct.ThrowIfCancellationRequested();

                // Имя мода — имя папки (нормализованное)
                var folderName = NormalizeFolderName(Path.GetFileName(candidateDir));
                if (folderName.Length == 0) continue;

                // Уже установлен — реактивируем (возвращаем в список профилей)
                var targetDir = Path.Combine(_modsPath, folderName);
                if (Directory.Exists(targetDir))
                {
                    if (GetModStatus(folderName) != ModStatus.Active)
                    {
                        await SetStatusAsync(folderName, ModStatus.Active).ConfigureAwait(false);
                        added.Add(folderName);
                        _logger.LogInformation("Мод {Folder} реактивирован", folderName);
                    }
                    else
                    {
                        skipped++;
                        _logger.LogInformation("Мод {Folder} уже активен, пропущен", folderName);
                    }
                    continue;
                }

                // Конвертируем zapret-hub-mod.json → manifest.json, если нужно
                var hubManifest = Path.Combine(candidateDir, "zapret-hub-mod.json");
                if (File.Exists(hubManifest) && !File.Exists(Path.Combine(candidateDir, "manifest.json")))
                    await ConvertHubManifestAsync(candidateDir, hubManifest, ct).ConfigureAwait(false);

                // Копируем в mods/
                Directory.CreateDirectory(targetDir);
                await CopyDirectoryAsync(candidateDir, targetDir, ct).ConfigureAwait(false);

                // Генерируем start.bat/stop.bat, если их нет
                var manifestPath = Path.Combine(targetDir, "manifest.json");
                var manifest = File.Exists(manifestPath)
                    ? JsonSerializer.Deserialize<ModManifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false))
                    : new ModManifest { Name = folderName, Version = "1.0.0", Author = "GitHub" };
                if (manifest != null)
                    await EnsureModScriptsAsync(targetDir, manifest, ct).ConfigureAwait(false);

                // Новый мод сразу активируем (появляется в главном списке профилей)
                await SetStatusAsync(folderName, ModStatus.Active).ConfigureAwait(false);

                // Копируем оригинальные .bat мода в engine/ — как стратегии (Power запустит через winws)
                if (!string.IsNullOrWhiteSpace(_engineDir) && Directory.Exists(_engineDir))
                    await CopyModBatsToEngineAsync(targetDir, _engineDir, candidateDir, ct).ConfigureAwait(false);

                added.Add(folderName);
                _logger.LogInformation("Установлен мод из GitHub: {Folder}", folderName);
            }

            await ScanModsAsync(ct).ConfigureAwait(false);
            return new ModSyncResult(added.Count, skipped, added);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Разбирает GitHub-URL на (owner, repo).
    /// </summary>
    private static (string Owner, string Repo) ParseGithubUrl(string repoUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);

        var trimmed = repoUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ghIndex = Array.FindIndex(parts, p => p.Contains("github.com", StringComparison.OrdinalIgnoreCase));
        if (ghIndex < 0 || ghIndex + 2 >= parts.Length)
            throw new InvalidOperationException("Укажите корректную ссылку на GitHub-репозиторий (https://github.com/owner/repo).");

        return (parts[ghIndex + 1], parts[ghIndex + 2]);
    }

    /// <summary>
    /// Скачивает ZIP-архив GitHub-репозитория с codeload (не зависит от zipball_url в API).
    /// </summary>
    private async Task<string> DownloadGithubZipAsync(string owner, string repo, CancellationToken ct)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Mods");

        // 1. Узнаём default branch (zipball_url в API-ответе может отсутствовать)
        string defaultBranch = "main";
        try
        {
            var apiJson = await client.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}", ct).ConfigureAwait(false);
            var apiDoc = System.Text.Json.Nodes.JsonNode.Parse(apiJson);
            defaultBranch = apiDoc?["default_branch"]?.GetValue<string>() ?? "main";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить данные репозитория GitHub, пробуем main");
        }

        // 2. Качаем ZIP напрямую с codeload
        var zipUrl = $"https://codeload.github.com/{owner}/{repo}/zip/refs/heads/{defaultBranch}";
        var zipPath = Path.Combine(Path.GetTempPath(), $"{owner}-{repo}-{DateTime.Now:HHmmss}.zip");
        try
        {
            var zipBytes = await client.GetByteArrayAsync(zipUrl, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(zipPath, zipBytes, ct).ConfigureAwait(false);
            return zipPath;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка скачивания архива GitHub: {Url}", zipUrl);
            throw new InvalidOperationException($"Не удалось скачать репозиторий: {ex.Message}");
        }
    }

    /// <summary>
    /// Импортирует содержимое распакованного архива: один мод или коллекцию модов.
    /// Поддерживает наш manifest.json и zapret-hub-mod.json (формат Zapret-Hub).
    /// </summary>
    private async Task<List<ModInfo>> ImportFromExtractedRootAsync(string extractedRoot, CancellationToken ct)
    {
        // Если внутри единственная папка (архив с корневым каталогом owner-repo-commit) — поднимаемся в неё
        var root = extractedRoot;
        var topDirs = Directory.GetDirectories(root);
        var topFiles = Directory.GetFiles(root);
        if (topFiles.Length == 0 && topDirs.Length == 1)
            root = topDirs[0];

        // Ищем кандидатов в моды: папки с manifest.json или zapret-hub-mod.json (на 1-2 уровня вглубь)
        var candidates = new List<(string Dir, string ManifestFile)>();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "manifest.json" or "zapret-hub-mod.json")
            {
                var dir = Path.GetDirectoryName(file)!;
                // Не забираем моды из вложенных коллекций глубже 2 уровней (например .github)
                var rel = Path.GetRelativePath(root, dir);
                if (rel.Split(Path.DirectorySeparatorChar).Length <= 2)
                    candidates.Add((dir, file));
            }
        }

        // Сортируем: сначала корневые, потом вложенные — чтобы корневой мод импортировался первым
        candidates = candidates
            .OrderBy(c => Path.GetRelativePath(root, c.Dir).Split(Path.DirectorySeparatorChar).Length)
            .ToList();

        if (candidates.Count == 0)
        {
            // Нет ни одного манифеста — импортируем весь корень как один мод
            _logger.LogWarning("В архиве не найдено манифестов, импортируем корень как один мод");
            var mod = await ImportFromFolderAsync(root, ct).ConfigureAwait(false);
            return new List<ModInfo> { mod };
        }

        var imported = new List<ModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var dir = candidates[i].Dir;
            // Если это родительская папка уже импортированного мода — пропускаем
            if (seen.Contains(dir)) continue;
            var skip = false;
            var parent = Path.GetDirectoryName(dir);
            while (parent != null && parent.Length >= root.Length)
            {
                if (seen.Contains(parent)) { skip = true; break; }
                if (parent == root) break;
                parent = Path.GetDirectoryName(parent);
            }
            if (skip) continue;

            // Конвертируем zapret-hub-mod.json → manifest.json
            if (Path.GetFileName(candidates[i].ManifestFile) == "zapret-hub-mod.json")
                await ConvertHubManifestAsync(dir, candidates[i].ManifestFile, ct).ConfigureAwait(false);

            var mod = await ImportFromFolderAsync(dir, ct).ConfigureAwait(false);
            seen.Add(dir);
            imported.Add(mod);
        }

        if (imported.Count == 0)
        {
            _logger.LogWarning("Не удалось импортировать ни одного мода из архива");
            var fallback = await ImportFromFolderAsync(root, ct).ConfigureAwait(false);
            imported.Add(fallback);
        }

        _logger.LogInformation("Импортировано модов из архива: {Count}", imported.Count);
        return imported;
    }

    /// <summary>
    /// Конвертирует zapret-hub-mod.json в наш manifest.json.
    /// </summary>
    private async Task ConvertHubManifestAsync(string modDir, string hubManifestPath, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(hubManifestPath, ct).ConfigureAwait(false);
            var doc = System.Text.Json.Nodes.JsonNode.Parse(json);
            var name = doc?["name"]?.GetValue<string>();
            var version = doc?["version"]?.GetValue<string>();
            var slug = doc?["slug"]?.GetValue<string>();
            var iconUrl = doc?["icon_url"]?.GetValue<string>();

            var manifest = new ModManifest
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(modDir) : name,
                Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
                Author = "Zapret-Hub",
                Description = "Импортировано из Zapret-Hub" + (string.IsNullOrWhiteSpace(slug) ? "" : $" ({slug})"),
                Dependencies = new List<string>(),
                Scripts = null,
                Config = new Dictionary<string, object>()
            };

            await File.WriteAllTextAsync(Path.Combine(modDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                ct).ConfigureAwait(false);

            // Генерируем start.bat/stop.bat, если их нет — чтобы мод был управляемым
            await EnsureModScriptsAsync(modDir, manifest, ct).ConfigureAwait(false);

            _logger.LogInformation("Сконвертирован zapret-hub-mod.json → manifest.json: {Dir}", modDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка конвертации zapret-hub-mod.json в {Dir}", modDir);
        }
    }

    /// <summary>
    /// Создаёт start.bat/stop.bat в папке мода, если они отсутствуют.
    /// start.bat запускает найденные .bat-скрипты мода (если есть) или помечает мод активным.
    /// stop.bat помечает мод неактивным.
    /// </summary>
    private async Task EnsureModScriptsAsync(string modDir, ModManifest manifest, CancellationToken ct)
    {
        var startPath = Path.Combine(modDir, "start.bat");
        var stopPath = Path.Combine(modDir, "stop.bat");
        var hasStart = manifest.Scripts?.Start != null || File.Exists(startPath);
        var hasStop = manifest.Scripts?.Stop != null || File.Exists(stopPath);

        if (hasStart && hasStop)
            return;

        // Ищем существующие .bat-скрипты мода (кроме start.bat/stop.bat)
        var batFiles = Directory.GetFiles(modDir, "*.bat")
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, "start.bat", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(f, "stop.bat", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!hasStart)
        {
            // Всегда маркер активного состояния — моды (Zapret-Hub) не запускают winws самостоятельно,
            // их .bat скрипты привязаны к engine/service.bat и падают при запуске из mods/
            var startContent = "@echo off\r\nrem FluxRoute: start " + manifest.Name + "\r\necho Mod active > .fluxroute-active\r\nexit /b 0\r\n";
            await File.WriteAllTextAsync(startPath, startContent, ct).ConfigureAwait(false);
            _logger.LogInformation("Создан start.bat для мода {Dir}", modDir);
        }

        if (!hasStop)
        {
            var stopContent = "@echo off\r\nrem FluxRoute: stop " + manifest.Name + "\r\nif exist .fluxroute-active del .fluxroute-active\r\nexit /b 0\r\n";
            await File.WriteAllTextAsync(stopPath, stopContent, ct).ConfigureAwait(false);
            _logger.LogInformation("Создан stop.bat для мода {Dir}", modDir);
        }

        // Обновляем манифест, чтобы скрипты были прописаны
        manifest.Scripts = new ModScripts { Start = "start.bat", Stop = "stop.bat" };
        await File.WriteAllTextAsync(Path.Combine(modDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ModInfo> ImportFromFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default)
    {
        if (filePaths == null || filePaths.Count == 0)
            throw new ArgumentException("Не выбрано ни одного файла.", nameof(filePaths));

        var folderName = $"mod-{DateTime.Now:yyyyMMddHHmmss}";
        var targetDir = Path.Combine(_modsPath, folderName);
        Directory.CreateDirectory(targetDir);

        foreach (var file in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            await Task.Run(() => File.Copy(file, dest, overwrite: true), ct).ConfigureAwait(false);
        }

        var manifest = new ModManifest
        {
            Name = Path.GetFileNameWithoutExtension(filePaths[0]),
            Version = "1.0.0",
            Author = "FluxRoute",
            Description = "Импортировано из файлов",
            Dependencies = new List<string>(),
            Scripts = new ModScripts { Start = "start.bat", Stop = "stop.bat" },
            Config = new Dictionary<string, object>()
        };
        await File.WriteAllTextAsync(Path.Combine(targetDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        _logger.LogInformation("Импортирован мод из файлов: {Folder}", targetDir);
        await ScanModsAsync(ct).ConfigureAwait(false);
        return _modCache.TryGetValue(folderName, out var mod)
            ? mod
            : new ModInfo { FolderName = folderName, Name = manifest.Name, Status = ModStatus.Inactive };
    }

    /// <summary>
    /// Рекурсивно копирует содержимое папки.
    /// </summary>
    private static async Task CopyDirectoryAsync(string sourceDir, string targetDir, CancellationToken ct)
    {
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var dest = file.Replace(sourceDir, targetDir);
            await Task.Run(() => File.Copy(file, dest, overwrite: true), ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> RunScriptAsync(string workingDir, string script, string scriptType, CancellationToken ct)
    {
        try
        {
            var parts = script.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var fileName = parts[0];
            var arguments = parts.Length > 1 ? parts[1] : string.Empty;

            if (!Path.IsPathRooted(fileName))
                fileName = Path.Combine(workingDir, fileName);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdout))
                _logger.LogInformation("[{Type}] stdout: {Output}", scriptType, stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("[{Type}] stderr: {Output}", scriptType, stderr.Trim());
            if (process.ExitCode != 0)
                _logger.LogError("[{Type}] exit code: {Code}", scriptType, process.ExitCode);

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Script error [{Type}]: {Script}", scriptType, script);
            return false;
        }
    }

    private async Task<ModManifest?> LoadManifestAsync(string folderName, CancellationToken ct)
    {
        var path = Path.Combine(_modsPath, folderName, "manifest.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ModManifest>(json);
    }

    private async Task SetStatusAsync(string folderName, ModStatus status, string? errorMessage = null)
    {
        await _statusLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _statusCache[folderName] = status;
            if (_modCache.TryGetValue(folderName, out var mod))
            {
                mod.Status = status;
                mod.ErrorMessage = errorMessage;
            }
            SaveStatuses();
        }
        finally { _statusLock.Release(); }
    }

    private async Task<bool> CheckDependenciesInternalAsync(string folderName, CancellationToken ct)
    {
        var manifest = await LoadManifestAsync(folderName, ct).ConfigureAwait(false);
        if (manifest?.Dependencies == null || manifest.Dependencies.Count == 0) return true;

        foreach (var dep in manifest.Dependencies)
            if (GetModStatus(dep) != ModStatus.Active)
                return false;

        return true;
    }

    private void SaveStatuses()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statusFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_statusFilePath,
                JsonSerializer.Serialize(_statusCache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save statuses to {Path}", _statusFilePath);
        }
    }

    private void LoadStatuses()
    {
        try
        {
            if (!File.Exists(_statusFilePath)) return;
            var json = File.ReadAllText(_statusFilePath);
            var statuses = JsonSerializer.Deserialize<Dictionary<string, ModStatus>>(json);
            if (statuses != null)
            {
                _statusCache.Clear();
                foreach (var kv in statuses) _statusCache[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load statuses from {Path}", _statusFilePath);
        }
    }

    public void Dispose() => _statusLock.Dispose();
}
