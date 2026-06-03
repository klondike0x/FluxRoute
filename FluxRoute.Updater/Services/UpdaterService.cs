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

public interface IUpdaterService
{
    string GetLocalVersion(string engineDir);
    Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string engineDir, CancellationToken ct = default);
    Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default);
    Task<bool> InstallUpdateAsync(string engineDir, UpdateInfo update, Action<string> onProgress, CancellationToken ct = default);
}

public interface IByeDpiUpdaterService
{
    string GetLocalVersion(string byedpiDir);
    Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string byedpiDir, CancellationToken ct = default);
    Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default);
    Task<bool> InstallUpdateAsync(string byedpiDir, UpdateInfo update, Action<string> onProgress, CancellationToken ct = default);
}

public sealed partial class ByeDpiUpdaterService : IByeDpiUpdaterService
{
    private const string ApiUrl = "https://api.github.com/repos/hufrea/byedpi/releases/latest";
    private const string VersionFile = "version.txt";
    private readonly IHttpClientFactory _httpClientFactory;

    public ByeDpiUpdaterService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ByeDpiUpdaterService() : this(new FluxRoute.Core.Services.DefaultHttpClientFactory()) { }

    public string GetLocalVersion(string byedpiDir)
    {
        var path = Path.Combine(byedpiDir, VersionFile);
        if (File.Exists(path))
        {
            try { return File.ReadAllText(path).Trim(); }
            catch { }
        }
        return "unknown";
    }

    private void SaveLocalVersion(string byedpiDir, string version)
    {
        var path = Path.Combine(byedpiDir, VersionFile);
        File.WriteAllText(path, version.TrimStart('v', 'V'));
    }

    public async Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            http.DefaultRequestHeaders.Remove("User-Agent");
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-ByeDpi-Updater");

            var json = await http.GetStringAsync(ApiUrl, ct).ConfigureAwait(false);

            var tagMatch = Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""");
            var urlMatch = Regex.Match(json, @"""browser_download_url""\s*:\s*""([^""]*byedpi[^""]*x86_64[^""]*w64\.zip)""", RegexOptions.IgnoreCase);
            if (!urlMatch.Success)
                urlMatch = Regex.Match(json, @"""browser_download_url""\s*:\s*""([^""]*byedpi[^""]*(?:exe|zip|7z))""", RegexOptions.IgnoreCase);
            var bodyMatch = Regex.Match(json, @"""body""\s*:\s*""([^""]*)""");

            if (!tagMatch.Success)
                return (null, "Не удалось найти tag_name в ответе GitHub API");

            var version = tagMatch.Groups[1].Value.TrimStart('v', 'V');
            var downloadUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
            var notes = bodyMatch.Success ? bodyMatch.Groups[1].Value : "";

            return (new UpdateInfo
            {
                Version = version,
                DownloadUrl = downloadUrl,
                ReleaseNotes = notes
            }, null);
        }
        catch (HttpRequestException ex) { return (null, $"Ошибка сети: {ex.Message}"); }
        catch (TaskCanceledException) { return (null, "Таймаут запроса"); }
        catch (Exception ex) { return (null, $"Ошибка: {ex.Message}"); }
    }

    public async Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string byedpiDir, CancellationToken ct = default)
    {
        var (release, error) = await GetLatestReleaseAsync(ct).ConfigureAwait(false);
        if (release is null) return (null, error);

        var local = GetLocalVersion(byedpiDir);
        if (local == release.Version)
            return (null, null);

        return (release, null);
    }

    public async Task<bool> InstallUpdateAsync(string byedpiDir, UpdateInfo update, Action<string> onProgress, CancellationToken ct = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), "byedpi_update.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), "byedpi_update_extract");

        try
        {
            onProgress($"⬇️ Скачиваем ByeDPI {update.Version}...");

            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            http.DefaultRequestHeaders.Remove("User-Agent");
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-ByeDpi-Updater");

            // Если DownloadUrl не найден через API, собираем вручную
            var url = string.IsNullOrEmpty(update.DownloadUrl)
                ? $"https://github.com/hufrea/byedpi/releases/download/v{update.Version}/byedpi-{update.Version.TrimStart('0', '.')}-x86_64-w64.zip"
                : update.DownloadUrl;

            var bytes = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);

            if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllBytesAsync(tempZip, bytes, ct).ConfigureAwait(false);
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, recursive: true);
                ZipFile.ExtractToDirectory(tempZip, tempExtract);

                var exeFile = Directory.EnumerateFiles(tempExtract, "ciadpi*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exeFile is null)
                {
                    onProgress("❌ ciadpi.exe не найден в архиве");
                    return false;
                }

                Directory.CreateDirectory(byedpiDir);
                File.Copy(exeFile, Path.Combine(byedpiDir, "ciadpi.exe"), overwrite: true);
            }
            else
            {
                Directory.CreateDirectory(byedpiDir);
                await File.WriteAllBytesAsync(Path.Combine(byedpiDir, "ciadpi.exe"), bytes, ct).ConfigureAwait(false);
            }

            SaveLocalVersion(byedpiDir, update.Version);
            onProgress($"✅ ByeDPI {update.Version} установлен!");
            return true;
        }
        catch (OperationCanceledException)
        {
            onProgress("⚠️ Обновление ByeDPI отменено.");
            return false;
        }
        catch (Exception ex)
        {
            onProgress($"❌ Ошибка: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
            try { Directory.Delete(tempExtract, recursive: true); } catch { }
        }
    }
}

public sealed partial class UpdaterService : IUpdaterService
{
    private const string RemoteVersionUrl =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

    private const string ZipUrlTemplate =
        "https://github.com/Flowseal/zapret-discord-youtube/releases/download/{0}/zapret-discord-youtube-{0}.zip";

    private const string VersionFile    = "version.txt";
    private const string StagingDirName = ".staging";
    private const string BackupDirName  = ".rollback";

    private readonly IHttpClientFactory _httpClientFactory;

    public UpdaterService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public UpdaterService() : this(new DefaultHttpClientFactory()) { }

    [GeneratedRegex(@"^set\s+""?LOCAL_VERSION=([^""]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalVersionRegex();

    private static string NormalizeVersion(string version)
        => version.Trim().TrimStart('v', 'V').Trim().ToLowerInvariant();

    public string GetLocalVersion(string engineDir)
    {
        var versionPath = Path.Combine(engineDir, VersionFile);
        if (File.Exists(versionPath))
        {
            try
            {
                var ver = NormalizeVersion(File.ReadAllText(versionPath));
                if (ver.Length > 0 && ver != "unknown")
                    return ver;
            }
            catch { }
        }

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
            catch { }
        }

        return "unknown";
    }

    private void SaveLocalVersion(string engineDir, string version)
    {
        File.WriteAllText(Path.Combine(engineDir, VersionFile), NormalizeVersion(version));
    }

    public async Task<(UpdateInfo? update, string? error)> CheckForUpdateAsync(string engineDir, CancellationToken ct = default)
    {
        var (release, error) = await GetLatestReleaseAsync(ct);
        if (release is null) return (null, error);

        var local = GetLocalVersion(engineDir);
        if (local == NormalizeVersion(release.Version)) return (null, null);

        return (release, null);
    }

    public async Task<(UpdateInfo? update, string? error)> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            var raw = await http.GetStringAsync(RemoteVersionUrl, ct);
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
        catch (HttpRequestException ex) { return (null, $"Ошибка сети: {ex.Message}"); }
        catch (TaskCanceledException) { return (null, "Таймаут запроса"); }
        catch (Exception ex) { return (null, $"Ошибка: {ex.Message}"); }
    }

    public async Task<bool> InstallUpdateAsync(
        string engineDir,
        UpdateInfo update,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        var tempZip     = Path.Combine(Path.GetTempPath(), "fluxroute_update.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), "fluxroute_update_extract");
        var stagingDir  = Path.Combine(engineDir, StagingDirName);
        var backupDir   = Path.Combine(engineDir, BackupDirName);

        try
        {
            onProgress($"📥 Источник: {update.DownloadUrl}");
            onProgress("⬇️ Скачиваем обновление...");

            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            var bytes = await http.GetByteArrayAsync(update.DownloadUrl, ct).ConfigureAwait(false);

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            onProgress($"🔒 SHA-256: {hash}");

            await File.WriteAllBytesAsync(tempZip, bytes, ct).ConfigureAwait(false);

            onProgress("📦 Распаковываем в staging-директорию...");
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
            Directory.CreateDirectory(stagingDir);

            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, recursive: true);
            ZipFile.ExtractToDirectory(tempZip, tempExtract);

            var extractedRoot = FindEngineRoot(tempExtract);
            if (extractedRoot is null)
            {
                onProgress("❌ Не удалось найти файлы в архиве.");
                return false;
            }

            CopyDirectoryToStaging(extractedRoot, stagingDir);
            onProgress($"✅ Staging подготовлен: {CountFiles(stagingDir)} файлов");

            if (!VerifyStaging(stagingDir, onProgress))
                return false;

            StopZapretService(onProgress);

            onProgress("💾 Создаём резервную копию engine/...");
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
            if (Directory.Exists(engineDir))
                CopyDirectoryToStaging(engineDir, backupDir, skipNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StagingDirName, BackupDirName });
            onProgress($"💾 Резервная копия создана: {CountFiles(backupDir)} файлов");

            onProgress("🔄 Применяем обновление...");
            var failedFiles = ApplyStaging(stagingDir, engineDir, onProgress);

            if (failedFiles.Count > 0)
            {
                onProgress($"⚠️ Не удалось записать {failedFiles.Count} файл(ов). Откатываемся...");
                var rolled = TryRollback(backupDir, engineDir, onProgress);
                onProgress(rolled ? "↩️ Откат выполнен." : "❌ Откат не удался — проверьте папку .rollback вручную.");
                return false;
            }

            SaveLocalVersion(engineDir, update.Version);
            onProgress($"✅ Обновление {NormalizeVersion(update.Version)} установлено!");

            TryDeleteDir(stagingDir);
            TryDeleteDir(backupDir);

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
            try { File.Delete(tempZip); } catch { }
            try { Directory.Delete(tempExtract, recursive: true); } catch { }
        }
    }

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

        Thread.Sleep(1500);
    }

    private static string? FindEngineRoot(string extractRoot)
    {
        foreach (var dir in Directory.EnumerateDirectories(extractRoot))
        {
            if (Directory.GetFiles(dir, "*.bat").Length > 0)
                return dir;

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                if (Directory.GetFiles(subDir, "*.bat").Length > 0)
                    return subDir;
            }
        }

        if (Directory.GetFiles(extractRoot, "*.bat").Length > 0)
            return extractRoot;

        return null;
    }

    private static bool VerifyStaging(string stagingDir, Action<string> onProgress)
    {
        var batFiles = Directory.GetFiles(stagingDir, "*.bat", SearchOption.AllDirectories);
        if (batFiles.Length == 0)
        {
            onProgress("❌ Staging не прошёл верификацию: *.bat файлы не найдены.");
            return false;
        }

        onProgress($"🔍 Верификация staging: *.bat файлов найдено — {batFiles.Length}");
        return true;
    }

    private static void CopyDirectoryToStaging(string source, string dest, HashSet<string>? skipNames = null)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var topSegment   = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

            if (skipNames is not null && skipNames.Contains(topSegment, StringComparer.OrdinalIgnoreCase))
                continue;

            var destFile = Path.Combine(dest, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static List<string> ApplyStaging(string stagingDir, string engineDir, Action<string> onProgress)
    {
        var failedFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            var fileName     = Path.GetFileName(file);
            if (IsUserFile(fileName)) continue;

            var relativePath = Path.GetRelativePath(stagingDir, file);
            var destFile     = Path.Combine(engineDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            try
            {
                File.Copy(file, destFile, overwrite: true);
            }
            catch (IOException)
            {
                try
                {
                    var tmp = destFile + ".upd";
                    File.Copy(file, tmp, overwrite: true);
                    File.Move(tmp, destFile, overwrite: true);
                }
                catch
                {
                    failedFiles.Add(relativePath);
                }
            }
        }

        if (failedFiles.Count > 0)
            onProgress($"⚠️ Не записаны: {string.Join(", ", failedFiles.Take(5))}");

        return failedFiles;
    }

    private static bool TryRollback(string backupDir, string engineDir, Action<string> onProgress)
    {
        if (!Directory.Exists(backupDir))
        {
            onProgress("❌ Backup-директория не найдена — откат невозможен.");
            return false;
        }

        onProgress("↩️ Откат: восстанавливаем файлы из .rollback...");
        var failed = new List<string>();

        foreach (var file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupDir, file);
            var destFile     = Path.Combine(engineDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            try
            {
                File.Copy(file, destFile, overwrite: true);
            }
            catch
            {
                failed.Add(relativePath);
            }
        }

        if (failed.Count > 0)
        {
            onProgress($"⚠️ Откат: не восстановлено {failed.Count} файл(ов).");
            return false;
        }

        return true;
    }

    private static int CountFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count()
            : 0;

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

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
