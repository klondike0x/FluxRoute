using System.IO;
using System.IO.Compression;
using System.Net.Http;
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

    static UpdaterService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
    }

    /// <summary>Читает текущую версию из engine/version.txt</summary>
    public string GetLocalVersion(string engineDir)
    {
        var path = Path.Combine(engineDir, VersionFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
    }

    /// <summary>Сохраняет версию в engine/version.txt</summary>
    private void SaveLocalVersion(string engineDir, string version)
    {
        File.WriteAllText(Path.Combine(engineDir, VersionFile), version);
    }

    /// <summary>Проверяет последний релиз на GitHub</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string engineDir, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            var json = await _http.GetStringAsync(url, ct);
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

            if (downloadUrl is null) return null;

            var local = GetLocalVersion(engineDir);
            if (local == tag) return null; // уже актуально

            return new UpdateInfo
            {
                Version = tag,
                DownloadUrl = downloadUrl,
                ReleaseNotes = body.Length > 500 ? body[..500] + "..." : body
            };
        }
        catch
        {
            return null; // нет интернета или API недоступен — тихо игнорируем
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

            // Шаг 4: Копируем файлы в engine/
            onProgress("🔄 Обновляем файлы engine/...");
            CopyDirectory(extractedRoot, engineDir, onProgress);

            // Шаг 5: Сохраняем версию
            SaveLocalVersion(engineDir, update.Version);
            onProgress($"✅ Обновление {update.Version} установлено!");
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

    /// <summary>Ищет папку с BAT файлами внутри распакованного архива</summary>
    private static string? FindEngineRoot(string extractRoot)
    {
        // Архив обычно: zapret-discord-youtube-1.9.7b/ -> внутри BAT файлы
        foreach (var dir in Directory.EnumerateDirectories(extractRoot))
        {
            if (Directory.GetFiles(dir, "*.bat").Length > 0)
                return dir;
        }
        // Или сразу в корне
        if (Directory.GetFiles(extractRoot, "*.bat").Length > 0)
            return extractRoot;

        return null;
    }

    private static void CopyDirectory(string source, string dest, Action<string> onProgress)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            // Пропускаем пользовательские файлы
            var fileName = Path.GetFileName(file);
            if (IsUserFile(fileName)) continue;

            var relativePath = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(dest, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            File.Copy(file, destFile, overwrite: true);
        }
    }

    /// <summary>Файлы которые НЕ перезаписываем при обновлении (пользовательские)</summary>
    private static bool IsUserFile(string fileName)
    {
        var userFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ipset-exclude-user.txt",
            "list-general-user.txt",
            "list-exclude-user.txt",
            "version.txt"
        };
        return userFiles.Contains(fileName);
    }
}