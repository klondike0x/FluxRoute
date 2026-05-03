using System.Diagnostics;
using System.IO;
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
    // Страница latest-релиза — GitHub делает 302 → /releases/tag/vX.Y.Z
    // Используем это вместо API, чтобы не упираться в лимит 60 запросов/час
    private const string LatestReleaseUrl =
        "https://github.com/klondike0x/FluxRoute/releases/latest";

    // Прямой URL скачивания по шаблону (без запроса списка assets)
    private const string DownloadUrlTemplate =
        "https://github.com/klondike0x/FluxRoute/releases/download/{0}/FluxRoute.exe";

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
            // Используем отдельный HttpClient с отключённым авто-редиректом,
            // чтобы прочитать Location-заголовок и получить тег версии из URL.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http    = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await http.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            // Ожидаем 301/302 с Location: .../releases/tag/v1.4.1
            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrWhiteSpace(location))
                return (null, "GitHub не вернул редирект на последний релиз");

            // Извлекаем тег из конца URL: "/releases/tag/v1.4.1" → "v1.4.1"
            var tagName = location.Split('/').LastOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
                return (null, $"Не удалось извлечь тег из URL: {location}");

            var remoteVersion = tagName.TrimStart('v', 'V');
            var localVersion  = GetCurrentVersion();

            if (string.Compare(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) <= 0)
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

    public async Task<(bool success, string? error)> DownloadAndApplyAsync(
        AppUpdateInfo update,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        var exePath   = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
            return (false, "Не удалось определить путь к исполняемому файлу");

        var exeDir    = Path.GetDirectoryName(exePath)!;
        var tempExe   = Path.Combine(Path.GetTempPath(), $"FluxRoute_{update.Version}.exe");
        var batPath   = Path.Combine(Path.GetTempPath(), "_FluxRoute_updater.bat");

        try
        {
            // ── 1. Скачиваем новый exe ─────────────────────────────────────
            onProgress($"⬇️ Скачиваем FluxRoute v{update.Version}...");

            using var http = _httpClientFactory.CreateClient(HttpClientNames.Updater);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

            var bytes = await http.GetByteArrayAsync(update.DownloadUrl, ct);
            var hash  = Convert.ToHexString(SHA256.HashData(bytes));
            onProgress($"🔒 SHA-256: {hash}");

            await File.WriteAllBytesAsync(tempExe, bytes, ct);
            onProgress("✅ Загрузка завершена");

            // ── 2. Записываем bat-заменщик ────────────────────────────────
            var pid        = Process.GetCurrentProcess().Id;
            var newExeName = Path.GetFileName(exePath);
            var newExePath = Path.Combine(exeDir, newExeName);

            // bat: ждёт завершения текущего процесса → заменяет exe → перезапускает
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
                copy /Y "{tempExe}" "{newExePath}" > nul
                if errorlevel 1 (
                    echo [FluxRoute Updater] Ошибка копирования!
                    pause
                    exit /b 1
                )
                echo [FluxRoute Updater] Запускаем FluxRoute v{update.Version}...
                start "" "{newExePath}"
                del "%~f0"
                """;

            await File.WriteAllTextAsync(batPath, bat, System.Text.Encoding.UTF8, ct);
            onProgress("🚀 Запускаем установщик...");

            // ── 3. Запускаем bat скрытым процессом ───────────────────────
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
