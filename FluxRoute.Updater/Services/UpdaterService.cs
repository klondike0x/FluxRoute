using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FluxRoute.Updater.Services;

public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
}

public sealed partial class UpdaterService
{
    // Flowseal хранит актуальную версию здесь — raw-файл, НЕ GitHub REST API (без лимита 60/час)
    private const string RemoteVersionUrl =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

    // Шаблон ссылки на ZIP-архив релиза (скачивание release asset — тоже без API лимита)
    // Тег и имя файла у Flowseal БЕЗ префикса 'v': /download/1.9.7b/zapret-discord-youtube-1.9.7b.zip
    private const string ZipUrlTemplate =
        "https://github.com/Flowseal/zapret-discord-youtube/releases/download/{0}/zapret-discord-youtube-{0}.zip";

    private const string VersionFile = "version.txt";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static UpdaterService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
    }

    [GeneratedRegex(@"^set\s+""?LOCAL_VERSION=([^""]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalVersionRegex();

    /// <summary>Нормализует версию: убирает префикс 'v', пробелы, приводит к lower</summary>
    private static string NormalizeVersion(string version)
        => version.Trim().TrimStart('v', 'V').Trim().ToLowerInvariant();

    /// <summary>
    /// Читает текущую версию — сначала version.txt (записывается нашим апдейтером),
    /// затем LOCAL_VERSION из service.bat как fallback (для первого запуска без обновлений)
    /// </summary>
    public string GetLocalVersion(string engineDir)
    {
        // Приоритет: version.txt — мы сами его пишем после успешного обновления
        var versionPath = Path.Combine(engineDir, VersionFile);
        if (File.Exists(versionPath))
        {
            try
            {
                var ver = NormalizeVersion(File.ReadAllText(versionPath));
                if (ver.Length > 0 && ver != "unknown")
                    return ver;
            }
            catch { /* fallback ниже */ }
        }

        // Fallback: LOCAL_VERSION из service.bat (до первого обновления через FluxRoute)
        var serviceBat = Path.Combine(engineDir, "service.bat");
        if (File.Exists(serviceBat))
        {
            try
            {
                foreach (var line in File.ReadLines(serviceBat))
                {
                    var match = LocalVersionRegex().Match(line);
                    if (match.Success)
                        return NormalizeVersion(match.Groups[1].Value);
                }
            }
            catch { /* fallback ниже */ }
        }

        return "unknown";
    }

    /// <summary>Сохраняет версию в engine/version.txt</summary>
    private void SaveLocalVersion(string engineDir, string version)
    {
        File.WriteAllText(Path.Combine(engineDir, VersionFile), NormalizeVersion(version));
    }

    /// <summary>
    /// Проверяет обновление через raw.githubusercontent.com — без API лимитов.
    /// Читает .service/version.txt из репозитория Flowseal.
    /// </summary>
    public async Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string engineDir, CancellationToken ct = default)
    {
        var (release, error) = await GetLatestReleaseAsync(ct);
        if (release is null) return (null, error);

        var local = GetLocalVersion(engineDir);
        if (local == NormalizeVersion(release.Version)) return (null, null);

        return (release, null);
    }

    /// <summary>
    /// Получает информацию о последнем релизе Flowseal.
    /// Версия — из raw.githubusercontent.com/.service/version.txt (без лимита).
    /// ZIP-ссылка — по шаблону (скачивание release asset, тоже без лимита).
    /// </summary>
    public async Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            // Один GET к статическому файлу — не тратит API rate limit
            var raw = await _http.GetStringAsync(RemoteVersionUrl, ct);
            var remoteVersion = raw.Trim();

            if (string.IsNullOrWhiteSpace(remoteVersion))
                return (null, "Пустая версия в .service/version.txt");

            var zipUrl = string.Format(ZipUrlTemplate, remoteVersion);

            return (new UpdateInfo
            {
                Version = remoteVersion,
                DownloadUrl = zipUrl,
                ReleaseNotes = ""
            }, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Ошибка сети: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, "Таймаут запроса");
        }
        catch (Exception ex)
        {
            return (null, $"Ошибка: {ex.Message}");
        }
    }

    /// <summary>Скачивает и устанавливает обновление</summary>
    public async Task<bool> InstallUpdateAsync(
        string engineDir,
        UpdateInfo update,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), "fluxroute_update.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), "fluxroute_update_extract");

        try
        {
            // Шаг 1: Скачиваем
            onProgress($"📥 Источник: {update.DownloadUrl}");
            onProgress("⬇️ Скачиваем обновление...");
            var bytes = await _http.GetByteArrayAsync(update.DownloadUrl, ct);
            await File.WriteAllBytesAsync(tempZip, bytes, ct);

            // SHA-256 для верификации — пользователь может сверить хеш
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            onProgress($"🔒 SHA-256: {hash}");

            // Шаг 2: Распаковываем во временную папку
            onProgress("📦 Распаковываем архив...");
            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, recursive: true);
            ZipFile.ExtractToDirectory(tempZip, tempExtract);

            // Шаг 3: Находим корень распакованного архива
            var extractedRoot = FindEngineRoot(tempExtract);
            if (extractedRoot is null)
            {
                onProgress("❌ Не удалось найти файлы в архиве.");
                return false;
            }

            // Шаг 4: Останавливаем службу zapret если запущена
            StopZapretService(onProgress);

            // Шаг 5: Копируем файлы в engine/
            onProgress("🔄 Обновляем файлы engine/...");
            var failedFiles = CopyDirectory(extractedRoot, engineDir, onProgress);

            if (failedFiles.Count > 0)
            {
                onProgress($"⚠️ Не удалось обновить {failedFiles.Count} файл(ов): {string.Join(", ", failedFiles.Take(5))}");
                onProgress("❌ Обновление не завершено. Остановите zapret и повторите.");
                return false;
            }

            // Шаг 6: Сохраняем версию только при полном успехе
            SaveLocalVersion(engineDir, update.Version);
            onProgress($"✅ Обновление {NormalizeVersion(update.Version)} установлено!");
            return true;
        }
        catch (OperationCanceledException)
        {
            onProgress("⚠️ Обновление отменено.");
            return false;
        }
        catch (Exception ex)
        {
            onProgress($"❌ Ошибка: {ex.Message}");
            return false;
        }
        finally
        {
            // Чистим временные файлы
            try { File.Delete(tempZip); } catch { }
            try { Directory.Delete(tempExtract, recursive: true); } catch { }
        }
    }

    /// <summary>Останавливаем службу zapret и убиваем winws.exe</summary>
    private static void StopZapretService(Action<string> onProgress)
    {
        try
        {
            using var sc = new System.Diagnostics.Process();
            sc.StartInfo = new System.Diagnostics.ProcessStartInfo("sc", "query zapret")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            sc.Start();
            var output = sc.StandardOutput.ReadToEnd();
            sc.WaitForExit(3000);

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                onProgress("⏹ Останавливаем службу zapret...");
                using var stop = new System.Diagnostics.Process();
                stop.StartInfo = new System.Diagnostics.ProcessStartInfo("net", "stop zapret")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                stop.Start();
                stop.WaitForExit(10000);
            }
        }
        catch { }

        // Убиваем winws.exe на случай если остался
        try
        {
            using var kill = new System.Diagnostics.Process();
            kill.StartInfo = new System.Diagnostics.ProcessStartInfo("taskkill", "/IM winws.exe /F")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            kill.Start();
            kill.WaitForExit(5000);
        }
        catch { }

        // Даём время на освобождение файлов
        Thread.Sleep(1500);
    }

    /// <summary>Ищет папку с BAT файлами внутри распакованного архива (до 2 уровней)</summary>
    private static string? FindEngineRoot(string extractRoot)
    {
        // Проверяем первый уровень вложенности
        foreach (var dir in Directory.EnumerateDirectories(extractRoot))
        {
            if (Directory.GetFiles(dir, "*.bat").Length > 0)
                return dir;

            // Проверяем второй уровень (для вложенных архивов)
            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                if (Directory.GetFiles(subDir, "*.bat").Length > 0)
                    return subDir;
            }
        }

        // Или сразу в корне
        if (Directory.GetFiles(extractRoot, "*.bat").Length > 0)
            return extractRoot;

        return null;
    }

    /// <summary>Копирует файлы, возвращает список файлов которые не удалось скопировать</summary>
    private static List<string> CopyDirectory(string source, string dest, Action<string> onProgress)
    {
        Directory.CreateDirectory(dest);
        var failedFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (IsUserFile(fileName)) continue;

            var relativePath = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(dest, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            try
            {
                File.Copy(file, destFile, overwrite: true);
            }
            catch (IOException)
            {
                // Файл заблокирован — пробуем через переименование
                try
                {
                    var backup = destFile + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(destFile, backup);
                    File.Copy(file, destFile, overwrite: true);
                    try { File.Delete(backup); } catch { }
                }
                catch
                {
                    failedFiles.Add(relativePath);
                }
            }
        }

        return failedFiles;
    }

    /// <summary>Файлы которые НЕ перезаписываем при обновлении (пользовательские)</summary>
    private static bool IsUserFile(string fileName)
    {
        var userFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ipset-exclude-user.txt",
            "list-general-user.txt",
            "list-exclude-user.txt"
        };
        return userFiles.Contains(fileName);
    }
}