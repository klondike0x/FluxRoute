using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

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

    /// <summary>Проверяет наличие нового релиза FluxRoute.
    /// Сначала GitHub REST API (с токеном если есть GH_TOKEN/GITHUB_TOKEN).
    /// При 403 или ошибке сети — авто-переключение на Atom-ленту (без лимитов).
    /// Результат кэшируется на 1 час.</summary>
    Task<(AppUpdateInfo? update, string? error)> CheckForAppUpdateAsync(CancellationToken ct = default);

    /// <summary>Скачивает новый exe и запускает bat-замену, затем завершает текущий процесс.</summary>
    Task<(bool success, string? error)> DownloadAndApplyAsync(AppUpdateInfo update, Action<string> onProgress, CancellationToken ct = default);
}

public class AppUpdaterService : IAppUpdaterService
{
    // GitHub REST API для получения последнего релиза
    private const string GitHubApiReleasesUrl =
        "https://api.github.com/repos/klondike0x/FluxRoute/releases/latest";

    // Atom-лента релизов — fallback при лимитах API (не является GitHub API, лимитов нет)
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

    public virtual string GetCurrentVersion()
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

        // ═══ ШАГ 1: Пробуем GitHub REST API ═══
        var (apiResult, apiError, apiWasRateLimited) = await TryGitHubApiAsync(ct).ConfigureAwait(false);

        if (apiResult is not null || apiError is null)
        {
            // API вернул результат (update или null = актуальная версия) — кэшируем и возвращаем
            _cachedResult = (apiResult, null);
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
        }

        // Если API вернул 403 (rate limit) или сетевую ошибку — логируем и падаем на Atom
        if (apiWasRateLimited || apiError is not null)
        {
            System.Diagnostics.Trace.TraceInformation(
                $"AppUpdater: GitHub API недоступен ({apiError}). Переключаюсь на Atom-ленту.");

            return await TryAtomFallbackAsync(ct).ConfigureAwait(false);
        }

        // Недостижимо, но на всякий случай
        return (null, apiError ?? "Неизвестная ошибка");
    }

    /// <summary>Пытается получить информацию о последнем релизе через GitHub REST API.
    /// Возвращает (null, null, false) если текущая версия актуальна.
    /// Возвращает (null, error, true) при 403 Rate Limit.
    /// Возвращает (null, error, false) при других ошибках.</summary>
    private async Task<(AppUpdateInfo? update, string? error, bool wasRateLimited)> TryGitHubApiAsync(CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientNames.AppUpdater);
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute/1.5.3");

            // Если задан токен через переменную окружения — используем (увеличивает лимит с 60 до 5000/ч)
            var token = Environment.GetEnvironmentVariable("GH_TOKEN")
                     ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await http.GetAsync(GitHubApiReleasesUrl, ct).ConfigureAwait(false);

            // ═══ 403 = Rate Limit Exceeded → Atom fallback ═══
            if ((int)response.StatusCode == 403)
            {
                var resetTime = response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
                    ? resetValues.FirstOrDefault()
                    : null;
                var errorMsg = "Превышен лимит GitHub API (403).";
                if (!string.IsNullOrWhiteSpace(token))
                    errorMsg += " Токен задан, но лимит исчерпан — подождите или проверьте токен.";
                else
                    errorMsg += " Без токена лимит 60 запросов/ч. Переключение на Atom...";
                return (null, errorMsg, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"GitHub API вернул {(int)response.StatusCode}", false);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Парсим tag_name из ответа
            using var doc = JsonDocument.Parse(json);
            var tagName = doc.RootElement.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return (null, "GitHub API: не найден tag_name", false);
            }

            if (!IsValidSemVerTag(tagName))
            {
                return (null, $"GitHub API: некорректный тег: {tagName}", false);
            }

            var remoteVersion = tagName.TrimStart('v', 'V');
            var localVersion = GetCurrentVersion();

            if (!System.Version.TryParse(remoteVersion, out var remote) ||
                !System.Version.TryParse(localVersion, out var local))
            {
                return (null, $"GitHub API: не удалось распознать версии: remote={remoteVersion}, local={localVersion}", false);
            }

            if (remote <= local)
            {
                return (null, null, false); // актуальная версия
            }

            return (new AppUpdateInfo
            {
                Version = remoteVersion,
                TagName = tagName,
                DownloadUrl = string.Format(DownloadUrlTemplate, tagName)
            }, null, false);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Ошибка сети при запросе к GitHub API: {ex.Message}", false);
        }
        catch (TaskCanceledException)
        {
            return (null, "Таймаут запроса к GitHub API", false);
        }
        catch (JsonException ex)
        {
            return (null, $"Ошибка парсинга ответа GitHub API: {ex.Message}", false);
        }
        catch (Exception ex)
        {
            return (null, $"GitHub API: {ex.Message}", false);
        }
    }

    /// <summary>Fallback: получает информацию о последнем релизе через Atom-ленту GitHub (без лимитов).</summary>
    private async Task<(AppUpdateInfo? update, string? error)> TryAtomFallbackAsync(CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientNames.AppUpdater);
            var xml = await http.GetStringAsync(ReleasesAtomUrl, ct).ConfigureAwait(false);

            var tagName = ParseLatestTagFromAtom(xml);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                _cachedResult = (null, "Atom: не удалось найти последний релиз");
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            if (!IsValidSemVerTag(tagName))
            {
                _cachedResult = (null, $"Atom: некорректный тег релиза: {tagName}");
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            var remoteVersion = tagName.TrimStart('v', 'V');
            var localVersion = GetCurrentVersion();

            if (!System.Version.TryParse(remoteVersion, out var remote) ||
                !System.Version.TryParse(localVersion, out var local))
            {
                _cachedResult = (null, $"Atom: не удалось распознать версии: remote={remoteVersion}, local={localVersion}");
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            if (remote <= local)
            {
                _cachedResult = (null, null);
                _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
                return _cachedResult;
            }

            _cachedResult = (new AppUpdateInfo
            {
                Version = remoteVersion,
                TagName = tagName,
                DownloadUrl = string.Format(DownloadUrlTemplate, tagName)
            }, null);
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
        }
        catch (HttpRequestException ex)
        {
            _cachedResult = (null, $"Atom: ошибка сети: {ex.Message}");
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
        }
        catch (TaskCanceledException)
        {
            _cachedResult = (null, "Atom: таймаут запроса");
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
        }
        catch (Exception ex)
        {
            _cachedResult = (null, $"Atom: ошибка: {ex.Message}");
            _cacheExpiresAt = DateTime.UtcNow + CacheTtl;
            return _cachedResult;
        }
    }

    /// <summary>
    /// Проверяет, что тег соответствует semver и не содержит суффиксов форков.
    /// </summary>
    private static bool IsValidSemVerTag(string tag)
    {
        var clean = tag.TrimStart('v', 'V');
        return Version.TryParse(clean, out _) &&
               !tag.Contains("AI", StringComparison.OrdinalIgnoreCase) &&
               !tag.Contains("fork", StringComparison.OrdinalIgnoreCase) &&
               !tag.Contains("custom", StringComparison.OrdinalIgnoreCase) &&
               !tag.Contains("-", StringComparison.Ordinal); // без pre-release суффиксов
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
