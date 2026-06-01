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

    /// <summary>Проверяет наличие нового релиза FluxRoute через Atom-ленту GitHub (без API, без лимитов).
    /// Результат кэшируется на 1 час — повторные вызовы не делают HTTP-запрос.</summary>
    Task<(AppUpdateInfo? update, string? error)> CheckForAppUpdateAsync(CancellationToken ct = default);

    /// <summary>Скачивает новый exe и запускает bat-замену, затем завершает текущий процесс.</summary>
    Task<(bool success, string? error)> DownloadAndApplyAsync(AppUpdateInfo update, Action<string> onProgress, CancellationToken ct = default);
}

public sealed class AppUpdaterService : IAppUpdaterService
{
    // Atom-лента релизов — не является GitHub API, лимитов нет
    private const string ReleasesAtomUrl =
        "https://github.com/klondike0x/FluxRoute/releases.atom";

    // Прямой URL скачивания (не API, CDN GitHub — лимитов нет)
    private const string DownloadUrlTemplate =
        "https://github.com/klondike0x/FluxRoute/releases/download/{0}/FluxRoute-{0}-portable.zip";

    // Не делать HTTP-запрос чаще одного раза в час
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;

    // In-memory кэш: сохраняем результат последней проверки
    private (AppUpdateInfo? update, string? error) _cachedResult;
    private DateTime _cacheExpiresAt = DateTime.MinValue;

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
        // Возвращаем кэш если TTL не истёк
        if (DateTime.UtcNow < _cacheExpiresAt)
            return _cachedResult;

        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientNames.AppUpdater);
            var xml = await http.GetStringAsync(ReleasesAtomUrl, ct);

            var tagName = ParseLatestTagFromAtom(xml);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                _cachedResult = (null, "Не удалось найти последний релиз в Atom-ленте");
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            var remoteVersion = tagName.TrimStart('v', 'V');
            var localVersion  = GetCurrentVersion();

            if (!System.Version.TryParse(remoteVersion, out var remote) ||
                !System.Version.TryParse(localVersion,  out var local))
            {
                _cachedResult = (null, $"Не удалось распознать версии: remote={remoteVersion}, local={localVersion}");
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            if (remote <= local)
            {
                _cachedResult = (null, null); // актуальная версия
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            _cachedResult = (new AppUpdateInfo
            {
                Version     = remoteVersion,
                TagName     = tagName,
                DownloadUrl = string.Format(DownloadUrlTemplate, tagName)
            }, null);
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
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
    /// Atom-лента GitHub: первый &lt;entry&gt; = самый свежий релиз.
    /// Формат id внутри entry: tag:github.com,2008:Repository/123456789/v1.4.1
    /// </summary>
    private static string? ParseLatestTagFromAtom(string xml)
    {
        const string entryOpen = "<entry>";
        const string idOpen    = "<id>tag:github.com,";
        const string idClose   = "</id>";

        var entryStart = xml.IndexOf(entryOpen, StringComparison.Ordinal);
        if (entryStart < 0) return null;

        var idStart = xml.IndexOf(idOpen, entryStart, StringComparison.Ordinal);
        if (idStart < 0) return null;

        var idEnd = xml.IndexOf(idClose, idStart, StringComparison.Ordinal);
        if (idEnd < 0) return null;

        var idContent = xml[idStart..idEnd];
        var lastSlash = idContent.LastIndexOf('/');
        if (lastSlash < 0) return null;

        return idContent[(lastSlash + 1)..].Trim();
    }

    public async Task<(bool success, string? error)> DownloadAndApplyAsync(
        AppUpdateInfo update,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
            return (false, "Не удалось определить путь к исполняемому файлу");

        // Предварительная проверка: exe доступен для чтения
        try
        {
            using var testStream = File.Open(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            return (false, $"Нет доступа к исполняемому файлу: {ex.Message}");
        }

        var exeDir   = Path.GetDirectoryName(exePath)!;
        var tempZip  = Path.Combine(Path.GetTempPath(), $"FluxRoute_{update.Version}.zip");
        var tempDir  = Path.Combine(Path.GetTempPath(), $"FluxRoute_{update.Version}_extracted");
        var batPath  = Path.Combine(Path.GetTempPath(), "_FluxRoute_updater.bat");

        try
        {
            // ── 1. Скачиваем zip ──────────────────────────────────────────
            onProgress($"⬇️ Скачиваем FluxRoute v{update.Version}...");

            using var http = _httpClientFactory.CreateClient(HttpClientNames.AppUpdater);

            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"Ошибка скачивания: {(int)response.StatusCode} {response.ReasonPhrase}");

            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var file   = File.Create(tempZip))
                await stream.CopyToAsync(file, ct);

            // Проверяем что ZIP не пустой
            var zipInfo = new FileInfo(tempZip);
            if (!zipInfo.Exists || zipInfo.Length < 1024)
                return (false, $"Скачанный файл повреждён или пуст (размер: {zipInfo.Length} байт)");

            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(tempZip, ct)));
            onProgress($"🔒 SHA-256: {hash}");
            onProgress("✅ Загрузка завершена");

            // ── 2. Распаковываем zip ──────────────────────────────────────
            onProgress("📦 Распаковываем архив...");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            ZipFile.ExtractToDirectory(tempZip, tempDir);

            var extractedExe = Directory.GetFiles(tempDir, "FluxRoute.exe", SearchOption.AllDirectories)
                                         .FirstOrDefault();
            if (extractedExe is null)
                return (false, "FluxRoute.exe не найден внутри архива");

            onProgress("✅ Архив распакован");

            // ── 3. Записываем bat-заменщик ────────────────────────────────
            var pid        = Process.GetCurrentProcess().Id;
            var newExeName = Path.GetFileName(exePath);
            var newExePath = Path.Combine(exeDir, newExeName);
            var extractedSourceDir = Path.GetDirectoryName(extractedExe)!;

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
                xcopy /E /Y /I "{extractedSourceDir}\*" "{exeDir}\"
                if errorlevel 1 (
                    echo [FluxRoute Updater] Ошибка копирования!
                    pause
                    exit /b 1
                )
                echo [FluxRoute Updater] Завершаем дочерние процессы...
                taskkill /IM winws.exe /F > nul 2>&1
                taskkill /IM WinDivert.exe /F > nul 2>&1
                net stop WinDivert > nul 2>&1
                wmic process where "ExecutablePath like '{exeDir.Replace("\\", "\\\\")}\\\\tg-proxy\\\\python\\\\python.exe'" delete > nul 2>&1
                echo [FluxRoute Updater] Очищаем временные файлы...
                del /F /Q "{tempZip}" > nul 2>&1
                rd /S /Q "{tempDir}" > nul 2>&1
                echo [FluxRoute Updater] Запускаем FluxRoute v{update.Version}...
                start "" "{newExePath}"
                del "%~f0"
                """;

            await File.WriteAllTextAsync(batPath, bat, System.Text.Encoding.UTF8, ct);
            onProgress("🚀 Запускаем установщик...");

            // ── 4. Запускаем bat через ShellExecute ───────────────────────
            var psi = new ProcessStartInfo
            {
                FileName        = batPath,
                WindowStyle     = ProcessWindowStyle.Hidden,
                UseShellExecute = true
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
