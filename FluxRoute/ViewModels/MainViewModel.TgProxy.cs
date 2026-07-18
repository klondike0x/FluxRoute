using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Services;
using FluxRoute.Updater.Services;
using FluxRoute.Views;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Application = System.Windows.Application;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    // ── Пути ──
    private const string PythonVersion = "3.14.5";
    private const string PythonEmbedUrl = $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";
    private const string PythonMirrorUrl = $"https://github.com/astral-sh/python-build-standalone/releases/download/20260510/cpython-{PythonVersion}%2B20260510-x86_64-pc-windows-msvc-install_only_stripped.tar.gz";

    private static readonly string[] PyPiMirrors =
    [
        "https://pypi.org/simple/",                      // основной (может быть заблокирован)
        "https://pypi.tuna.tsinghua.edu.cn/simple/",     // Tsinghua University, Китай
        "https://mirrors.aliyun.com/pypi/simple/",       // Alibaba Cloud
        "https://pypi.mirrors.ustc.edu.cn/simple/",      // USTC, Китай
    ];

    private string TgProxyDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tg-proxy");
    private string PythonDir => Path.Combine(TgProxyDir, "python");
    private string PythonExe
    {
        get
        {
            // Если директория не существует — возвращаем путь (File.Exists вернёт false)
            if (!Directory.Exists(PythonDir))
                return Path.Combine(PythonDir, "python.exe");

            // 1. Прямой путь (embeddable)
            var direct = Path.Combine(PythonDir, "python.exe");
            if (File.Exists(direct)) return direct;

            // 2. Поиск в подпапках (install_only может иметь вложенность)
            var found = Directory.GetFiles(PythonDir, "python.exe", SearchOption.AllDirectories).FirstOrDefault();
            return found ?? direct;
        }
    }
    private string ProxyScriptDir => Path.Combine(TgProxyDir, "proxy");
    private string ProxyScript => Path.Combine(ProxyScriptDir, "tg_ws_proxy.py");

    private const string TgProxyReleasesAtomUrl = MirrorUrls.TgProxyReleasesAtom;

    // ═══ v1.6.0 (#60): Fallback-зеркала для tg-proxy ═══
    private static readonly string[] TgProxyTagFallbackUrls =
    {
        MirrorUrls.TgProxyReleasesAtom,
        MirrorUrls.TgProxyReleasesAtomMirrorSf, // SourceForge-зеркало
    };

    private static readonly string[] TgProxyZipFallbackUrlsTemplate =
    {
        MirrorUrls.TgProxyZipTemplate,
        MirrorUrls.TgProxyZipTemplateMirrorSf, // SourceForge-зеркало
    };
    // ════════════════════════════════════════════════════

    // ── Состояние ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyRunning;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyInstalled;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool isTgProxyDownloading;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDownloadStatus = "";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyVersion = "—";

    private Process? _tgProxyProcess;

    public ObservableCollection<string> TgProxyLogs { get; } = new();

    // ── Настройки ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyHost = "127.0.0.1";
    partial void OnTgProxyHostChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyPort = "1443";
    partial void OnTgProxyPortChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxySecret = "";
    partial void OnTgProxySecretChanged(string value) => SaveSettings();

    // Оставлено только для совместимости со старыми fluxroute-settings.json.
    // В UI, аргументах запуска и Telegram-ссылке SNI-домен больше не используется.
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDomain = "";
    partial void OnTgProxyDomainChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyVerbose = false;
    partial void OnTgProxyVerboseChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyPreferIPv4 = true;
    partial void OnTgProxyPreferIPv4Changed(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyAutoStartOnAppLaunch = true;
    partial void OnTgProxyAutoStartOnAppLaunchChanged(bool value) => SaveSettings();

    // DC → IP
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDcIps = "2:149.154.167.220\n4:149.154.167.220";
    partial void OnTgProxyDcIpsChanged(string value) => SaveSettings();

    // Cloudflare Proxy
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfEnabled = true;
    partial void OnTgProxyCfEnabledChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfPriority = true;
    partial void OnTgProxyCfPriorityChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfDomainEnabled = false;
    partial void OnTgProxyCfDomainEnabledChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyCfDomain = "";
    partial void OnTgProxyCfDomainChanged(string value) => SaveSettings();

    // Производительность
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyBufKb = "256";
    partial void OnTgProxyBufKbChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyPoolSize = "4";
    partial void OnTgProxyPoolSizeChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyLogMaxMb = "5.0";
    partial void OnTgProxyLogMaxMbChanged(string value) => SaveSettings();

    // ── Текст кнопки запуска ──
    public string TgProxyToggleText => TgProxyRunning ? "⏹ Остановить прокси" : "▶ Запустить прокси";
    partial void OnTgProxyRunningChanged(bool value) => OnPropertyChanged(nameof(TgProxyToggleText));

    // ── Инициализация при первом входе на вкладку ──
    private bool _tgProxyTabVisited = false;

    public void OnTgProxyTabActivated()
    {
        if (_tgProxyTabVisited)
            return;

        _tgProxyTabVisited = true;
        EnsureTgProxyStateInitialized();

        if (!TgProxyInstalled)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CustomDialog.Show(
                            " TG WS Proxy",
                            "Компонент TG WS Proxy не установлен.\n\nБудет скачано:\n• Python Embeddable (~12 МБ)\n• Исходники прокси (~50 КБ)\n• Пакет cryptography\n\nЗагрузить сейчас?",
                            "Загрузить",
                            "Отмена"))
                    {
                        _ = DownloadTgProxyAsync();
                    }
                });
            }
        }
    }

    public void InitializeTgProxyOnStartup()
    {
        // ═══ v1.6.0: Fix #56 — убиваем зависшие python.exe от предыдущего запуска ═══
        KillOrphanedTgProxyProcesses();
        // ════════════════════════════════════════════════════════════════════════════

        EnsureTgProxyStateInitialized();

        if (!TgProxyAutoStartOnAppLaunch || !TgProxyInstalled || TgProxyRunning)
            return;

        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("⏭ TG WS Proxy автозапуск пропущен: secret не задан.");
            return;
        }

        StartTgProxy();
    }

    // ═══ v1.6.0: Fix #56 — Автоматическое завершение зависших TG WS Proxy процессов ═══
    /// <summary>
    /// Сканирует все процессы python.exe и убивает те, что запущены из папки tg-proxy.
    /// Это решает проблему занятия порта 1443 после некорректного завершения предыдущего запуска.
    /// </summary>
    private static void KillOrphanedTgProxyProcesses()
    {
        try
        {
            var tgProxyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tg-proxy")
                + Path.DirectorySeparatorChar;
            var killed = 0;

            foreach (var proc in Process.GetProcessesByName("python"))
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)
                        || !exePath.StartsWith(tgProxyDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Trace.TraceInformation(
                        $"🗑 Найден зависший python.exe (PID {proc.Id}) от предыдущего запуска. Завершаю...");

                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                    killed++;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Игнорируем ошибки доступа к чужим процессам python.exe (не из нашей папки)
                    // и процессы, которые уже завершились к моменту проверки
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            if (killed > 0)
                Trace.TraceInformation($"🗑 Завершено зависших процессов python.exe: {killed}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Graceful degradation: ошибка сканирования процессов не должна ломать запуск приложения
            Trace.TraceError($"⚠ Ошибка при сканировании зависших процессов TG Proxy: {ex.Message}");
        }
    }
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private void EnsureTgProxyStateInitialized()
    {
        TgProxyInstalled = File.Exists(PythonExe) && File.Exists(ProxyScript);
        TgProxyVersion = TgProxyInstalled ? GetTgProxyLocalVersion() : "—";
    }

    [RelayCommand]
    private async Task DownloadTgProxyAsync()
    {
        IsTgProxyDownloading = true;
        AddTgProxyLog("⬇️ Начало установки TG WS Proxy...");
        try
        {
            Directory.CreateDirectory(TgProxyDir);
            Directory.CreateDirectory(ProxyScriptDir);

            // ✅ ИСПРАВЛЕНО: используем HttpClient из DI (SSL/TLS настроен в App.xaml.cs)
            using var http = _httpClientFactory.CreateClient("TgProxyDownloader");

            // ═══ ШАГ 1: Python с fallback на astral-sh mirror ═══
            if (!File.Exists(PythonExe))
            {
                TgProxyDownloadStatus = "⬇️ Скачиваем Python 3.14.5...";
                var pythonInstalled = await DownloadPythonWithFallbackAsync(http);
                if (!pythonInstalled)
                {
                    TgProxyDownloadStatus = "❌ Не удалось скачать Python";
                    AddTgProxyLog("❌ Не удалось скачать Python ни с одного источника");
                    AddTgProxyLog("💡 Скачайте вручную через Firefox:");
                    AddTgProxyLog("   https://www.python.org/ftp/python/3.14.0/python-3.14.0-embed-amd64.zip");
                    AddTgProxyLog("   И распакуйте в: tg-proxy\\python\\");
                    return;
                }
            }

            // Всегда обновляем .pth для embeddable-сборки
            FixPythonPth();

            // ═══ ШАГ 2: cryptography (pip уже есть в install_only сборке) ═══
            var pipExe = Directory.Exists(PythonDir)
                ? Directory.GetFiles(PythonDir, "pip.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            pipExe ??= Path.Combine(PythonDir, "Scripts", "pip.exe");

            if (!File.Exists(pipExe))
            {
                TgProxyDownloadStatus = "⬇️ Устанавливаем pip...";
                AddTgProxyLog("📦 Устанавливаем pip через get-pip.py...");
                if (!await InstallPipWithFallbackAsync(http))
                {
                    TgProxyDownloadStatus = "❌ Ошибка установки pip";
                    AddTgProxyLog("❌ Не удалось установить pip — проверь доступ к интернету");
                    return;
                }
                pipExe = Directory.Exists(PythonDir)
                    ? Directory.GetFiles(PythonDir, "pip.exe", SearchOption.AllDirectories).FirstOrDefault()
                    : null;
                pipExe ??= Path.Combine(PythonDir, "Scripts", "pip.exe");
            }
            else
            {
                AddTgProxyLog($"✅ pip уже есть: {Path.GetRelativePath(TgProxyDir, pipExe)}");
            }

            // ═══ ШАГ 3: cryptography ═══
            var sitePackages = Path.Combine(PythonDir, "Lib", "site-packages");
            var cryptoDir = Directory.Exists(sitePackages)
                ? Directory.GetDirectories(sitePackages, "cryptography*").FirstOrDefault()
                : null;

            if (cryptoDir is null)
            {
                TgProxyDownloadStatus = "📦 Устанавливаем cryptography...";
                if (!await InstallPipPackageWithFallbackAsync(pipExe!, "cryptography"))
                {
                    TgProxyDownloadStatus = "❌ Ошибка установки cryptography";
                    AddTgProxyLog("⚠️ cryptography не установлен — прокси может не работать");
                    // Не прерываем установку — пользователь может установить вручную
                }
            }

            // ═══ ШАГ 4: Скачиваем ВЕСЬ репозиторий и распаковываем proxy/ ═══
            TgProxyDownloadStatus = "⬇️ Скачиваем исходники прокси...";
            AddTgProxyLog("📦 Скачиваем репозиторий tg-ws-proxy...");
            var tagName = await GetLatestTgProxyTagAsync(http) ?? "main";

            if (!await DownloadAndExtractProxyFolderAsync(http, tagName))
            {
                AddTgProxyLog("❌ Не удалось скачать исходники прокси");
                return;
            }

            File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);
            TgProxyVersion = tagName;
            TgProxyInstalled = true;
            EnsureTgProxyDomainsInHostlist();
            TgProxyDownloadStatus = $"✅ Установлено {tagName}";
            AddTgProxyLog($"✅ Исходники proxy/ скачаны ({tagName})");
            AddTgProxyLog("🎉 TG WS Proxy готов к работе!");

            if (string.IsNullOrWhiteSpace(TgProxySecret))
                GenerateTgProxySecret();

            if (TgProxyRunning)
            {
                AddTgProxyLog("🔄 Перезапускаем прокси с новой установкой...");
                StopTgProxy();
                await Task.Delay(1000);
                StartTgProxy();
            }
        }
        catch (Exception ex)
        {
            TgProxyDownloadStatus = $"❌ Ошибка: {ex.Message}";
            AddTgProxyLog($"❌ Ошибка установки: {ex.Message}");
            if (ex.InnerException is not null)
                AddTgProxyLog($"   Inner: {ex.InnerException.Message}");
        }
        finally
        {
            IsTgProxyDownloading = false;
        }
    }

    /// <summary>
    /// Скачивает ZIP-архив всего репозитория tg-ws-proxy и распаковывает папку proxy/.
    /// Это надёжнее, чем скачивание отдельных файлов — защищает от добавления новых файлов в репозиторий.
    /// </summary>
    private async Task<bool> DownloadAndExtractProxyFolderAsync(HttpClient http, string tagName)
    {
        var tempZipPath = Path.Combine(TgProxyDir, "tg-ws-proxy-temp.zip");
        var tempExtractDir = Path.Combine(TgProxyDir, "tg-ws-proxy-extracted");

        // ═══ v1.6.0 (#60): Пробуем основной URL → fallback-зеркала ═══
        for (var i = 0; i < TgProxyZipFallbackUrlsTemplate.Length; i++)
        {
            var zipUrl = string.Format(TgProxyZipFallbackUrlsTemplate[i], tagName);
            var label = i == 0 ? "основной источник" : $"зеркало #{i}";

            try
            {
                AddTgProxyLog($"   URL ({label}): {zipUrl}");

                // Скачиваем ZIP
                using var response = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using (var contentStream = await response.Content.ReadAsStreamAsync())
                await using (var fileStream = File.Create(tempZipPath))
                {
                    await contentStream.CopyToAsync(fileStream);
                }

                AddTgProxyLog($"✅ Архив скачан через {label} ({new FileInfo(tempZipPath).Length / 1024} KB)");

                // Распаковываем во временную директорию
                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, recursive: true);

                Directory.CreateDirectory(tempExtractDir);
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                // Ищем папку proxy/ внутри распакованного архива
                var extractedRoot = Directory.GetDirectories(tempExtractDir).FirstOrDefault();
                if (extractedRoot is null)
                {
                    if (i < TgProxyZipFallbackUrlsTemplate.Length - 1) continue;
                    AddTgProxyLog("❌ Архив пуст");
                    return false;
                }

                var proxySourceDir = Path.Combine(extractedRoot, "proxy");
                if (!Directory.Exists(proxySourceDir))
                {
                    if (i < TgProxyZipFallbackUrlsTemplate.Length - 1) continue;
                    AddTgProxyLog("❌ Папка proxy/ не найдена в архиве");
                    return false;
                }

                // Копируем proxy/ в целевую директорию
                if (Directory.Exists(ProxyScriptDir))
                    Directory.Delete(ProxyScriptDir, recursive: true);

                CopyDirectory(proxySourceDir, ProxyScriptDir);

                var fileCount = Directory.GetFiles(ProxyScriptDir, "*", SearchOption.AllDirectories).Length;
                AddTgProxyLog($"✅ Исходники proxy/ распакованы ({fileCount} файлов)");

                // Сохраняем версию
                File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);
                TgProxyVersion = tagName;

                return true;
            }
            catch (Exception ex) when (i < TgProxyZipFallbackUrlsTemplate.Length - 1)
            {
                AddTgProxyLog($"⚠️ {label} недоступен: {ex.Message}");
                // Продолжаем к следующему зеркалу
            }
            catch (Exception ex)
            {
                AddTgProxyLog($"❌ Ошибка скачивания tg-proxy (все источники): {ex.Message}");
                return false;
            }
            finally
            {
                // Чистим временные файлы между попытками
                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
                try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, recursive: true); } catch { }
            }
        }
        // ═══════════════════════════════════════════════════════════════

        AddTgProxyLog("❌ Не удалось скачать tg-proxy ни с одного источника");
        return false;
    }

    /// <summary>
    /// Рекурсивное копирование директории.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    /// <summary>
    /// Скачивает Python с fallback: python.org → astral-sh mirror (GitHub CDN, не блокируется DPI).
    /// </summary>
    private async Task<bool> DownloadPythonWithFallbackAsync(HttpClient http)
    {
        // Попытка 1: python.org (embeddable)
        var embedUrl = PythonEmbedUrl;
        try
        {
            AddTgProxyLog("📦 Попытка 1/2: python.org (embeddable ZIP)...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var bytes = await http.GetByteArrayAsync(embedUrl, cts.Token);  // ← используем http из параметра!

            var zipPath = Path.Combine(TgProxyDir, "python_embed.zip");
            await File.WriteAllBytesAsync(zipPath, bytes);

            Directory.CreateDirectory(PythonDir);
            ZipFile.ExtractToDirectory(zipPath, PythonDir, overwriteFiles: true);
            File.Delete(zipPath);
            AddTgProxyLog($"✅ Python {PythonVersion} (embeddable) скачан и распакован");
            return true;
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"⚠️ python.org недоступен: {ex.Message}");
            AddTgProxyLog("🔄 Переключаемся на зеркало astral-sh (GitHub)...");
        }

        // Попытка 2: astral-sh mirror (install_only_stripped.tar.gz)
        var mirrorUrl = PythonMirrorUrl;
        try
        {
            AddTgProxyLog("📦 Попытка 2/2: astral-sh mirror (TAR.GZ, ~21MB)...");
            var tarPath = Path.Combine(TgProxyDir, "python.tar.gz");

            using var response = await http.GetAsync(mirrorUrl, HttpCompletionOption.ResponseHeadersRead);  // ← http из параметра!
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync();

            // ✅ ВАЖНО: закрываем поток ПЕРЕД запуском tar.exe
            await using (var fileStream = File.Create(tarPath))
            {
                var buffer = new byte[81920];
                long bytesRead = 0;
                int lastPercent = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        var percent = (int)(bytesRead * 100 / totalBytes);
                        if (percent >= lastPercent + 20)
                        {
                            AddTgProxyLog($"   ⬇️ {percent}% ({bytesRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)");
                            lastPercent = percent;
                        }
                    }
                }
                await fileStream.FlushAsync();
            } // ← Здесь fileStream закрывается!

            AddTgProxyLog($"✅ Скачано {new FileInfo(tarPath).Length / 1024 / 1024}MB");
            AddTgProxyLog("📦 Распаковываем через tar.exe...");
            Directory.CreateDirectory(PythonDir);

            var psi = new ProcessStartInfo
            {
                FileName = "tar.exe",
                Arguments = $"-xzf \"{tarPath}\" -C \"{PythonDir}\"",  // БЕЗ --strip-components=1
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var tarProcess = Process.Start(psi);
            if (tarProcess is null)
                throw new Exception("Не удалось запустить tar.exe");

            var stderr = await tarProcess.StandardError.ReadToEndAsync();
            await tarProcess.WaitForExitAsync();

            if (tarProcess.ExitCode != 0)
                throw new Exception($"tar.exe завершился с кодом {tarProcess.ExitCode}: {stderr}");

            File.Delete(tarPath);

            // Ищем python.exe (install_only имеет вложенную структуру python/python/)
            var pythonExeFound = Directory.GetFiles(PythonDir, "python.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (pythonExeFound is null)
                throw new Exception("python.exe не найден после распаковки");

            AddTgProxyLog($"✅ Python {PythonVersion} (astral-sh) успешно установлен");
            AddTgProxyLog($"   Путь: {Path.GetRelativePath(TgProxyDir, pythonExeFound)}");
            return true;
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ astral-sh mirror тоже недоступен: {ex.Message}");
            return false;
        }
    }

    private async Task RunProcessAsync(string exe, string args, string workDir, Dictionary<string, string>? extraEnv = null, bool ignoreExitCode = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (extraEnv != null)
        {
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;
        }

        using var proc = Process.Start(psi) ?? throw new Exception($"Не удалось запустить {exe}");
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        if (!ignoreExitCode && proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} завершился с кодом {proc.ExitCode}");
    }

    /// <summary>
    /// Обновляет .pth файл embeddable Python для подключения site-packages и proxy-скриптов.
    /// 
    /// ВАЖНО: этот метод безопасен для обеих сборок Python:
    /// - embeddable (python.org) → метод находит и правит python3XX._pth
    /// - install_only (astral-sh) → метода ._pth нет, и он просто выходит (return)
    /// 
    /// Также учитывает вложенную структуру install_only сборки.
    /// </summary>
    private void FixPythonPth()
    {
        // Если директория не существует — выходим
        if (!Directory.Exists(PythonDir))
            return;

        var pthFile = Directory.GetFiles(PythonDir, "python*._pth", SearchOption.AllDirectories).FirstOrDefault();
        if (pthFile is null)
            return;

        var pthDir = Path.GetDirectoryName(pthFile)!;
        var sitePackages = Path.Combine(pthDir, "Lib", "site-packages");
        Directory.CreateDirectory(sitePackages);

        var pthName = Path.GetFileNameWithoutExtension(pthFile);
        var zipName = $"{pthName}.zip";

        var lines = new List<string>
    {
        ".",
        zipName,
        sitePackages,
        ProxyScriptDir,
        "import site"
    };

        File.WriteAllLines(pthFile, lines);
    }

    /// <summary>
    /// Переменные окружения для корректной работы Python с пакетами.
    /// 
    /// ВАЖНО: для install_only сборки (astral-sh) структура вложенная:
    /// tg-proxy/python/python/python.exe
    /// tg-proxy/python/python/Lib/
    /// tg-proxy/python/python/Scripts/
    /// 
    /// Поэтому PYTHONHOME должен указывать на директорию с python.exe,
    /// а не на PythonDir.
    /// </summary>
    private Dictionary<string, string> GetPythonEnv()
    {
        var pythonExePath = PythonExe;
        var pythonHome = Path.GetDirectoryName(pythonExePath) ?? PythonDir;
        var sitePackages = Path.Combine(pythonHome, "Lib", "site-packages");
        var scripts = Path.Combine(pythonHome, "Scripts");

        return new Dictionary<string, string>
        {
            // PYTHONHOME — директория, где лежит python.exe (может быть вложенной)
            ["PYTHONHOME"] = pythonHome,
            ["PYTHONPATH"] = $"{ProxyScriptDir};{sitePackages}",
            ["PATH"] = $"{pythonHome};{scripts};{Environment.GetEnvironmentVariable("PATH")}"
        };
    }

    private string GetTgProxyLocalVersion()
    {
        var versionFile = Path.Combine(TgProxyDir, "version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
    }

    // ── Генерация Secret (dd + 32 hex = dd-prefix + 16 байт) ──
    [RelayCommand]
    private void GenerateTgProxySecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        TgProxySecret = "dd" + Convert.ToHexString(bytes).ToLowerInvariant();
        AddTgProxyLog(" Secret сгенерирован");
    }

    // ── Запуск / Остановка ──
    [RelayCommand]
    private void ToggleTgProxy()
    {
        if (TgProxyRunning)
            StopTgProxy();
        else
            StartTgProxy();
    }

    private void StartTgProxy()
    {
        if (IsTgProxyDownloading)
        {
            AddTgProxyLog("⏳ Идёт установка, подождите завершения...");
            return;
        }

        if (!File.Exists(PythonExe) || !File.Exists(ProxyScript))
        {
            AddTgProxyLog("❌ Компонент не установлен.\nНажмите «Обновления».");
            return;
        }

        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("❌ Secret не задан. Нажмите для генерации.");
            return;
        }

        EnsureTgProxyDomainsInHostlist();

        var scriptArgs = BuildArguments();

        // Запускаем: python.exe proxy/tg_ws_proxy.py
        // -m не подходит т.к. нет пакета, запускаем скрипт напрямую.
        var fullArgs = $"\"{ProxyScript}\" {scriptArgs}";
        AddTgProxyLog($" python {fullArgs}");

        var psi = new ProcessStartInfo
        {
            FileName = PythonExe,
            Arguments = fullArgs,
            WorkingDirectory = TgProxyDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var kv in GetPythonEnv())
            psi.Environment[kv.Key] = kv.Value;

        try
        {
            _tgProxyProcess = Process.Start(psi);
            if (_tgProxyProcess is null)
            {
                AddTgProxyLog("❌ Не удалось запустить процесс");
                return;
            }

            _tgProxyProcess.OutputDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
            _tgProxyProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
            _tgProxyProcess.BeginOutputReadLine();
            _tgProxyProcess.BeginErrorReadLine();

            TgProxyRunning = true;
            AddTgProxyLog($"▶ TG WS Proxy запущен (PID {_tgProxyProcess.Id})");
            AddTgProxyLog($" Слушает: {TgProxyHost}:{TgProxyPort}");
            _ = WatchTgProxyProcessAsync(_tgProxyProcess);
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка запуска: {ex.Message}");
        }
    }

    private string BuildArguments()
    {
        var args = new System.Text.StringBuilder();

        // Python-скрипт принимает только 32 hex-символа (без dd/ee-префикса).
        var rawSecret = TgProxySecret.StartsWith("dd", StringComparison.OrdinalIgnoreCase)
            ? TgProxySecret[2..]
            : TgProxySecret;

        args.Append($"--host {TgProxyHost}");
        args.Append($" --port {TgProxyPort}");
        args.Append($" --secret {rawSecret}");

        // SNI/Fake-TLS домен намеренно не передаём: tg-ws-proxy в FluxRoute работает через обычный dd-secret.

        // DC → IP
        foreach (var line in TgProxyDcIps.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var dc = line.Trim();
            if (!string.IsNullOrEmpty(dc))
                args.Append($" --dc-ip {dc}");
        }

        // Cloudflare
        if (!TgProxyCfEnabled)
            args.Append(" --no-cfproxy");
        else if (!TgProxyCfPriority)
            args.Append(" --cfproxy-priority false");

        if (TgProxyCfDomainEnabled && !string.IsNullOrWhiteSpace(TgProxyCfDomain))
            args.Append($" --cfproxy-domain {TgProxyCfDomain.Trim()}");

        // Производительность
        if (int.TryParse(TgProxyBufKb, out var bufKb) && bufKb != 256)
            args.Append($" --buf-kb {bufKb}");

        if (int.TryParse(TgProxyPoolSize, out var poolSize) && poolSize != 4)
            args.Append($" --pool-size {poolSize}");

        if (double.TryParse(TgProxyLogMaxMb, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var logMb) && logMb != 5.0)
            args.Append($" --log-max-mb {logMb.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        if (TgProxyVerbose)
            args.Append(" -v");

        return args.ToString();
    }

    private void AppendTgLog(string line)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            Application.Current.Dispatcher.Invoke(() => AddTgProxyLog(line));
    }

    private async Task WatchTgProxyProcessAsync(Process proc)
    {
        try
        {
            await proc.WaitForExitAsync();
        }
        catch (Exception)
        {
            // процесс удалён через StopTgProxy
        }

        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TgProxyRunning = false;
                int? code = null;

                try
                {
                    code = proc.ExitCode;
                }
                catch (Exception)
                {
                    // disposed
                }

                AddTgProxyLog(code.HasValue
                    ? $"⏹ TG WS Proxy остановлен (код: {code})"
                    : "⏹ TG WS Proxy остановлен");
            });
        }
    }

    private void StopTgProxy()
    {
        try
        {
            if (_tgProxyProcess is { HasExited: false })
            {
                _tgProxyProcess.Kill(entireProcessTree: true);
                _tgProxyProcess.Dispose();
                _tgProxyProcess = null;
            }
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"⚠ Ошибка остановки: {ex.Message}");
        }

        TgProxyRunning = false;
        AddTgProxyLog("⏹ TG WS Proxy остановлен");
    }

    [RelayCommand]
    private async Task CheckTgProxyUpdates()
    {
        AddTgProxyLog("🔍 Проверяем обновления TG WS Proxy...");
        try
        {
            using var http = _httpClientFactory.CreateClient("TgProxyDownloader");
            var latest = await GetLatestTgProxyTagAsync(http);

            // ✅ Graceful degradation: если не можем определить тег — не пытаемся обновляться
            if (!IsValidGitTag(latest))
            {
                AddTgProxyLog("❌ Не удалось определить последнюю версию TG WS Proxy");
                AddTgProxyLog("💡 Возможно, GitHub недоступен из текущей сети");
                return;
            }

            var local = GetTgProxyLocalVersion();
            if (latest == local)
            {
                AddTgProxyLog($"✅ Актуальная версия ({local})");
            }
            else
            {
                AddTgProxyLog($"⬆️ Доступна версия {latest} (текущая {local})");
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    var update = Application.Current.Dispatcher.Invoke(() =>
                        CustomDialog.Show("🔄 Обновление",
                            $"Доступна версия {latest}.\nОбновить исходники прокси?",
                            "Обновить", "Отмена"));
                    if (update)
                        await UpdateProxySourcesAsync(latest);
                }
            }
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка проверки: {ex.Message}");
        }
    }

    private async Task UpdateProxySourcesAsync(string tagName)
    {
        if (!IsValidGitTag(tagName))
        {
            AddTgProxyLog($"❌ Некорректный тег версии: '{tagName}'");
            return;
        }

        AddTgProxyLog($"⬇️ Обновляем исходники до {tagName}...");
        try
        {
            using var http = _httpClientFactory.CreateClient("TgProxyDownloader");

            if (await DownloadAndExtractProxyFolderAsync(http, tagName))
            {
                TgProxyVersion = tagName;
                AddTgProxyLog($"✅ Исходники обновлены до {tagName}");

                // Перезапускаем прокси, если он запущен
                if (TgProxyRunning)
                {
                    AddTgProxyLog("🔄 Перезапускаем прокси с новыми исходниками...");
                    StopTgProxy();
                    await Task.Delay(1000);
                    StartTgProxy();
                }
            }
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка обновления: {ex.Message}");
        }
    }

    /// <summary>
    /// Надёжное получение тега последнего релиза TG WS Proxy:
    /// 1) redirect releases/latest (быстро)
    /// 2) Atom feed (без лимитов API, без редиректов — запасной путь)
    /// </summary>
    private async Task<string?> GetLatestTgProxyTagAsync(HttpClient http, CancellationToken ct = default)
    {
        // ═══ v1.6.0 (#60): Цепочка получения тега: GitHub → SourceForge → Atom ═══

        // Способ 1: редирект releases/latest (GitHub)
        try
        {
            using var noRedirectHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var noRedirectHttp = new HttpClient(noRedirectHandler);
            noRedirectHttp.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            noRedirectHttp.Timeout = TimeSpan.FromSeconds(10);

            var response = await noRedirectHttp.GetAsync(
                MirrorUrls.TgProxyLatestRelease, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Redirect
                                     or System.Net.HttpStatusCode.MovedPermanently
                                     or System.Net.HttpStatusCode.TemporaryRedirect
                                     or System.Net.HttpStatusCode.PermanentRedirect
                && response.Headers.Location is { } location)
            {
                var tag = location.ToString().Split('/').LastOrDefault();
                if (IsValidGitTag(tag)) return tag;
            }
        }
        catch { /* fallback */ }

        // Способ 1a: редирект releases/latest (SourceForge-зеркало)
        try
        {
            using var noRedirectHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var noRedirectHttp = new HttpClient(noRedirectHandler);
            noRedirectHttp.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            noRedirectHttp.Timeout = TimeSpan.FromSeconds(10);

            var response = await noRedirectHttp.GetAsync(
                MirrorUrls.TgProxyLatestReleaseMirrorSf, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Redirect
                                     or System.Net.HttpStatusCode.MovedPermanently
                                     or System.Net.HttpStatusCode.TemporaryRedirect
                                     or System.Net.HttpStatusCode.PermanentRedirect
                && response.Headers.Location is { } location)
            {
                var tag = location.ToString().Split('/').LastOrDefault();
                if (IsValidGitTag(tag)) return tag;
            }
        }
        catch { /* fallback */ }

        // Способ 2: Atom feed (GitHub, надёжно, без лимитов API)
        try
        {
            var xml = await http.GetStringAsync(TgProxyReleasesAtomUrl, ct);
            return ParseTgProxyTagFromAtom(xml);
        }
        catch { /* fallback */ }

        // Способ 2a: Atom feed (SourceForge-зеркало)
        try
        {
            var xml = await http.GetStringAsync(MirrorUrls.TgProxyReleasesAtomMirrorSf, ct);
            return ParseTgProxyTagFromAtom(xml);
        }
        catch { return null; }
        // ═══════════════════════════════════════════════════════════════
    }

    private static string? ParseTgProxyTagFromAtom(string xml)
    {
        const string entryOpen = "<entry>";
        const string idOpen = "<id>tag:github.com,";
        const string idClose = "</id>";

        var entryStart = xml.IndexOf(entryOpen, StringComparison.Ordinal);
        if (entryStart < 0) return null;

        var idStart = xml.IndexOf(idOpen, entryStart, StringComparison.Ordinal);
        if (idStart < 0) return null;

        var idEnd = xml.IndexOf(idClose, idStart, StringComparison.Ordinal);
        if (idEnd < 0) return null;

        var idContent = xml[idStart..idEnd];
        var lastSlash = idContent.LastIndexOf('/');
        if (lastSlash < 0) return null;

        var tag = idContent[(lastSlash + 1)..].Trim();
        return IsValidGitTag(tag) ? tag : null;
    }

    private static bool IsValidGitTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        if (tag == "?") return false;
        if (tag.Contains('/') || tag.Contains('\\')) return false;
        if (tag.Contains("..")) return false;  // защита от path traversal
        return tag.Length >= 2 && tag.Length <= 64;
    }

    [RelayCommand]
    private void ClearTgProxyLogs() => TgProxyLogs.Clear();

    private void AddTgProxyLog(string msg)
    {
        TgProxyLogs.Add(msg);
        while (TgProxyLogs.Count > 500)
            TgProxyLogs.RemoveAt(0);
    }

    public void StopTgProxyOnExit() => StopTgProxy();

    private string TgDeepLink => $"tg://proxy?server=127.0.0.1&port={TgProxyPort}&secret={TgProxySecret}";

    [RelayCommand]
    private void OpenInTelegram()
    {
        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("Secret not set.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(TgDeepLink) { UseShellExecute = true });
            AddTgProxyLog("Opening Telegram with proxy settings...");
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyTgLink()
    {
        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("Secret not set.");
            return;
        }

        System.Windows.Clipboard.SetText(TgDeepLink);
        AddTgProxyLog($"Copied: {TgDeepLink}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FALLBACK ДЛЯ УСТАНОВКИ PIP И CRYPTOGRAPHY (БЛОКИРОВКА PyPI)
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly string[] TgProxyRequiredDomains =
    [
        "pypi.org",
        "files.pythonhosted.org",
        "bootstrap.pypa.io",
        "www.python.org",
        "pythonhosted.org",
        "github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "github.githubassets.com",
        "api.github.com"
    ];

    /// <summary>
    /// Устанавливает pip через get-pip.py с перебором зеркал PyPI.
    /// </summary>
    private async Task<bool> InstallPipWithFallbackAsync(HttpClient http, CancellationToken ct = default)
    {
        var getPipUrl = "https://bootstrap.pypa.io/get-pip.py";
        var getPipPath = Path.Combine(TgProxyDir, "get-pip.py");

        try
        {
            var getPipBytes = await http.GetByteArrayAsync(getPipUrl, ct);
            await File.WriteAllBytesAsync(getPipPath, getPipBytes, ct);
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Не удалось скачать get-pip.py: {ex.Message}");
            return false;
        }

        for (var i = 0; i < PyPiMirrors.Length; i++)
        {
            var mirror = PyPiMirrors[i];
            var mirrorName = new Uri(mirror).Host;
            var attemptLabel = i == 0 ? "основной" : $"зеркало {i}/{PyPiMirrors.Length - 1}";

            AddTgProxyLog($"🔄 Устанавливаем pip ({attemptLabel}: {mirrorName})...");
            TgProxyDownloadStatus = $"⬇️ pip через {mirrorName}...";

            try
            {
                // get-pip.py поддерживает --index-url
                var args = $"\"{getPipPath}\" --index-url {mirror} --trusted-host {new Uri(mirror).Host}";
                var exitCode = await RunProcessWithExitCodeAsync(
                    PythonExe, args, PythonDir,
                    extraEnv: GetPythonEnv(),
                    timeoutSeconds: 120,
                    ct: ct);

                if (exitCode == 0)
                {
                    AddTgProxyLog($"✅ pip установлен через {mirrorName}");
                    return true;
                }

                AddTgProxyLog($"⚠️ {mirrorName} вернул код {exitCode}, пробуем следующий...");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AddTgProxyLog($"⚠️ {mirrorName} не сработал: {ex.Message}");
            }
        }

        AddTgProxyLog("❌ Не удалось установить pip ни через одно зеркало");
        return false;
    }

    /// <summary>
    /// Устанавливает pip-пакет с перебором зеркал PyPI.
    /// </summary>
    private async Task<bool> InstallPipPackageWithFallbackAsync(
        string pipExe,
        string packageName,
        CancellationToken ct = default)
    {
        for (var i = 0; i < PyPiMirrors.Length; i++)
        {
            var mirror = PyPiMirrors[i];
            var mirrorName = new Uri(mirror).Host;
            var attemptLabel = i == 0 ? "основной" : $"зеркало {i}/{PyPiMirrors.Length - 1}";

            AddTgProxyLog($"📦 Устанавливаем {packageName} ({attemptLabel}: {mirrorName})...");

            try
            {
                var args = $"install {packageName} --quiet --no-warn-script-location " +
                           $"--index-url {mirror} --trusted-host {new Uri(mirror).Host}";
                var exitCode = await RunProcessWithExitCodeAsync(
                    pipExe, args, PythonDir,
                    extraEnv: GetPythonEnv(),
                    timeoutSeconds: 180,
                    ct: ct);

                if (exitCode == 0)
                {
                    AddTgProxyLog($"✅ {packageName} установлен через {mirrorName}");
                    return true;
                }

                AddTgProxyLog($"⚠️ {mirrorName} вернул код {exitCode}, пробуем следующий...");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AddTgProxyLog($"⚠️ {mirrorName} не сработал: {ex.Message}");
            }
        }

        AddTgProxyLog($"❌ Не удалось установить {packageName} ни через одно зеркало");
        return false;
    }

    /// <summary>
    /// Запускает процесс и возвращает код выхода (а не выбрасывает исключение).
    /// </summary>
    private async Task<int> RunProcessWithExitCodeAsync(
        string exe,
        string args,
        string workDir,
        Dictionary<string, string>? extraEnv = null,
        int timeoutSeconds = 120,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (extraEnv != null)
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Не удалось запустить {exe}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Процесс {exe} не завершился за {timeoutSeconds}с");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr) && stderr.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            AddTgProxyLog($"⚠️ {Path.GetFileName(exe)}: {stderr.Split('\n').FirstOrDefault()?.Trim()}");
        }

        return proc.ExitCode;
    }

    /// <summary>
    /// Добавляет домены PyPI и GitHub в list-general-user.txt, чтобы zapret обрабатывал их трафик.
    /// </summary>
    private void EnsureTgProxyDomainsInHostlist()
    {
        try
        {
            var listsDir = Path.Combine(EngineDir, "lists");
            Directory.CreateDirectory(listsDir);
            var userHostlistPath = Path.Combine(listsDir, "list-general-user.txt");

            var existingDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(userHostlistPath))
            {
                foreach (var line in File.ReadLines(userHostlistPath))
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrWhiteSpace(domain) && !domain.StartsWith('#'))
                        existingDomains.Add(domain);
                }
            }

            // Добавляем пользовательские домены (из менеджера доменов)
            foreach (var domain in CustomTargetDomains)
                if (!string.IsNullOrWhiteSpace(domain))
                    existingDomains.Add(domain.Trim());

            // Добавляем наши обязательные домены
            var addedCount = 0;
            foreach (var domain in TgProxyRequiredDomains)
            {
                if (existingDomains.Add(domain))
                    addedCount++;
            }

            File.WriteAllLines(userHostlistPath, existingDomains.OrderBy(d => d), new UTF8Encoding(false));

            if (addedCount > 0)
                AddTgProxyLog($"✅ Добавлено {addedCount} домен(ов) для обхода PyPI/GitHub в list-general-user.txt");
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"⚠️ Ошибка добавления доменов: {ex.Message}");
        }
    }
}
