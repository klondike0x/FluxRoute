using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FluxRoute.Updater.Services;

public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
}

public sealed class UpdaterService
{
    private const string Owner = "Flowseal";
    private const string Repo = "zapret-discord-youtube";
    private const string VersionFile = "version.txt";

    private static readonly HttpClient _http = new();

    // ETag-кэш для conditional requests (не тратят rate limit)
    private string? _cachedETag;
    private string? _cachedResponseBody;

    static UpdaterService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
    }

    /// <summary>Нормализует версию: убирает префикс 'v', пробелы, приводит к lower</summary>
    private static string NormalizeVersion(string version)
        => version.Trim().TrimStart('v', 'V').Trim().ToLowerInvariant();

    /// <summary>Читает текущую версию из engine/version.txt</summary>
    public string GetLocalVersion(string engineDir)
    {
        var path = Path.Combine(engineDir, VersionFile);
        return File.Exists(path) ? NormalizeVersion(File.ReadAllText(path)) : "unknown";
    }

    /// <summary>Сохраняет версию в engine/version.txt</summary>
    private void SaveLocalVersion(string engineDir, string version)
    {
        File.WriteAllText(Path.Combine(engineDir, VersionFile), NormalizeVersion(version));
    }

    /// <summary>Проверяет последний релиз на GitHub</summary>
    public async Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string engineDir, CancellationToken ct = default)
    {
        var (release, error) = await GetLatestReleaseAsync(ct);
        if (release is null) return (null, error);

        var local = GetLocalVersion(engineDir);
        if (local == NormalizeVersion(release.Version)) return (null, null); // уже актуально

        return (release, null);
    }

    /// <summary>Получает последний релиз без сравнения версий (для принудительного обновления)</summary>
    public async Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Conditional request — если ETag не изменился, GitHub вернёт 304 (не тратит rate limit)
            if (_cachedETag is not null)
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(_cachedETag));

            var response = await _http.SendAsync(request, ct);

            // 304 Not Modified — используем кэшированный ответ
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && _cachedResponseBody is not null)
            {
                return ParseRelease(_cachedResponseBody);
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Парсим сообщение об ошибке от GitHub
                try
                {
                    using var errDoc = JsonDocument.Parse(json);
                    var msg = errDoc.RootElement.GetProperty("message").GetString() ?? "";
                    if (msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                        return (null, "GitHub API: превышен лимит запросов (60/час). Подождите ~час и попробуйте снова.");
                    return (null, $"GitHub API: {msg}");
                }
                catch
                {
                    return (null, $"GitHub API: HTTP {(int)response.StatusCode}");
                }
            }

            // Сохраняем ETag для будущих conditional requests
            if (response.Headers.ETag?.Tag is { } etag)
            {
                _cachedETag = etag;
                _cachedResponseBody = json;
            }

            return ParseRelease(json);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Ошибка сети: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, "Таймаут запроса к GitHub");
        }
        catch (Exception ex)
        {
            return (null, $"Ошибка: {ex.Message}");
        }
    }

    /// <summary>Парсит JSON-ответ релиза GitHub</summary>
    private static (UpdateInfo? update, string? error) ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var body = root.GetProperty("body").GetString() ?? "";

        // Ищем .zip ассет
        string? downloadUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (downloadUrl is null) return (null, "В релизе нет .zip архива");

        return (new UpdateInfo
        {
            Version = tag,
            DownloadUrl = downloadUrl,
            ReleaseNotes = body.Length > 500 ? body[..500] + "..." : body
        }, null);
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
            onProgress("⬇️ Скачиваем обновление...");
            var bytes = await _http.GetByteArrayAsync(update.DownloadUrl, ct);
            await File.WriteAllBytesAsync(tempZip, bytes, ct);

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