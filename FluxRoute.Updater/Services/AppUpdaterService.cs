using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;

namespace FluxRoute.Updater.Services;

/// <summary>Информация о доступном обновлении самого приложения FluxRoute.</summary>
public sealed class AppUpdateInfo
{
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string TagName { get; init; } = "";
}

public interface IAppUpdaterService
{
    /// <summary>Возвращает текущую версию из сборки (например "1.4.0").</summary>
    string GetCurrentVersion();

    /// <summary>Проверяет наличие нового релиза FluxRoute через редирект GitHub Releases (без API, без лимитов).</summary>
    Task<(AppUpdateInfo? update, string? error)> CheckForAppUpdateAsync(CancellationToken ct = default);

    /// <summary>Скачивает новый exe и запускает bat-замену, затем завершает текущий процесс.</summary>
    Task<(bool success, string? error)> DownloadAndApplyAsync(AppUpdateInfo update, Action<string> onProgress, CancellationToken ct = default);
}

public sealed class AppUpdaterService : IAppUpdaterService
{
    // Atom-лента релизов — не является GitHub API, лимитов нет
    private const string ReleasesAtomUrl =
        "https://github.com/klondike0x/FluxRoute/releases.atom";

    // Имя asset в релизе: FluxRoute-v1.4.1-portable.zip
    private const string AssetNameTemplate = "FluxRoute-{0}-portable.zip";

    // Прямой URL скачивания (не API, CDN GitHub — лимитов нет)
    private const string DownloadUrlTemplate =
        "https://github.com/klondike0x/FluxRoute/releases/download/{0}/FluxRoute-{0}-portable.zip";

    private const string UserAgent = "FluxRoute-AppUpdater";

    private readonly IHttpClientFactory _httpClientFactory;

    public AppUpdaterService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Fallback для дизайнера / тестов.</summary>
    public AppUpdaterService() : this(new DefaultHttpClientFactory()) { }

    public string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<(AppUpdateInfo? update, string? error)> CheckForAppUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

            var xml = await http.GetStringAsync(ReleasesAtomUrl, ct);

            // Парсим Atom XML — первый <id> в <entry> содержит тег:
            // tag:github.com,2008:Repository/123456789/v1.4.1
            var tagName = ParseLatestTagFromAtom(xml);
            if (string.IsNullOrWhiteSpace(tagName))
                return (null, "Не удалось найти последний релиз в Atom-ленте");

            var remoteVersion = tagName.TrimStart('v', 'V');
            var localVersion  = GetCurrentVersion();

            // Корректное сравнение через Version, не строковое
            if (!System.Version.TryParse(remoteVersion, out var remote) ||
                !System.Version.TryParse(localVersion,  out var local))
                return (null, $"Не удалось распознать версии: remote={remoteVersion}, local={localVersion}");

            if (remote <= local)
                return (null, null); // актуальная версия

            return (new AppUpdateInfo
            {
                Version     = remoteVersion,
                TagName     = tagName,
                DownloadUrl = string.Format(DownloadUrlTemplate, tagName)
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

    /// <summary>
    /// Извлекает тег последнего релиза из Atom XML.
    /// Формат id: tag:github.com,2008:Repository/123/v1.4.1
    /// </summary>
    private static string? ParseLatestTagFromAtom(string xml)
    {
        // Используем простой поиск по тексту — не тянем XML-парсер,
        // структура Atom от GitHub стабильна.
        const string entryIdOpen  = "<id>tag:github.com,";
        const string entryIdClose = "</id>";

        var start = xml.IndexOf(entryIdOpen, StringComparison.Ordinal);
        if (start < 0) return null;

        // Пропускаем до конца блока "tag:github.com,2008:Repository/NNNN/"
        var slashIndex = xml.IndexOf('/', start);
        if (slashIndex < 0) return null;

        // Ещё один слеш — после ID репозитория идёт тег
        var tagStart = xml.IndexOf('/', slashIndex + 1);
        if (tagStart < 0) return null;
        tagStart++; // пропускаем сам '/'

        var tagEnd = xml.IndexOf(entryIdClose, tagStart, StringComparison.Ordinal);
        if (tagEnd < 0) return null;

        return xml[tagStart..tagEnd].Trim();
    }

    public async Task<(bool success, string? error)> DownloadAndApplyAsync(
        AppUpdateInfo update,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
            return (false, "Не удалось определить путь к исполняемому файлу");

        var exeDir   = Path.GetDirectoryName(exePath)!;
        var tempZip  = Path.Combine(Path.GetTempPath(), $"FluxRoute_{update.Version}.zip");
        var tempDir  = Path.Combine(Path.GetTempPath(), $"FluxRoute_{update.Version}_extracted");
        var batPath  = Path.Combine(Path.GetTempPath(), "_FluxRoute_updater.bat");

        try
        {
            // ── 1. Скачиваем zip ──────────────────────────────────────────
            onProgress($"⬇️ Скачиваем FluxRoute v{update.Version}...");

            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

            var bytes = await http.GetByteArrayAsync(update.DownloadUrl, ct);
            var hash  = Convert.ToHexString(SHA256.HashData(bytes));
            onProgress($"🔒 SHA-256: {hash}");

            await File.WriteAllBytesAsync(tempZip, bytes, ct);
            onProgress("✅ Загрузка завершена");

            // ── 2. Распаковываем zip ──────────────────────────────────────
            onProgress("📦 Распаковываем архив...");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempDir);

            // Ищем FluxRoute.exe внутри архива (может быть в подпапке)
            var extractedExe = Directory.GetFiles(tempDir, "FluxRoute.exe", SearchOption.AllDirectories)
                                         .FirstOrDefault();
            if (extractedExe is null)
                return (false, "FluxRoute.exe не найден внутри архива");

            onProgress("✅ Архив распакован");

            // ── 3. Записываем bat-заменщик ────────────────────────────────
            var pid        = Process.GetCurrentProcess().Id;
            var newExeName = Path.GetFileName(exePath);
            var newExePath = Path.Combine(exeDir, newExeName);

            var bat = $"""
                @echo off
                chcp 65001 > nul
                echo [FluxRoute Updater] Ожидаем завершения процесса PID {pid}...
                :waitloop
                tasklist /FI "PID eq {pid}" 2>NUL | find /I "{pid}" > NUL
                if not errorlevel 1 (
                    timeout /t 1 /nobreak > nul
                    goto waitloop
                )
                echo [FluxRoute Updater] Устанавливаем v{update.Version}...
                copy /Y "{extractedExe}" "{newExePath}" > nul
                if errorlevel 1 (
                    echo [FluxRoute Updater] Ошибка копирования!
                    pause
                    exit /b 1
                )
                echo [FluxRoute Updater] Очищаем временные файлы...
                del /F /Q "{tempZip}" > nul 2>&1
                rd /S /Q "{tempDir}" > nul 2>&1
                echo [FluxRoute Updater] Запускаем FluxRoute v{update.Version}...
                start "" "{newExePath}"
                del "%~f0"
                """;

            await File.WriteAllTextAsync(batPath, bat, System.Text.Encoding.UTF8, ct);
            onProgress("🚀 Запускаем установщик...");

            // ── 4. Запускаем bat скрытым процессом ───────────────────────
            var psi = new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/C \"{batPath}\"",
                WindowStyle     = ProcessWindowStyle.Hidden,
                CreateNoWindow  = true,
                UseShellExecute = false
            };
            Process.Start(psi);

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка обновления: {ex.Message}");
        }
    }

    }
