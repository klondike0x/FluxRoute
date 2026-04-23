using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    // ── Пути ──
    private string TgProxyDir     => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tg-proxy");
    private string PythonDir      => Path.Combine(TgProxyDir, "python");
    private string PythonExe      => Path.Combine(PythonDir, "python.exe");
    private string ProxyScriptDir => Path.Combine(TgProxyDir, "proxy");
    private string ProxyScript    => Path.Combine(ProxyScriptDir, "tg_ws_proxy.py");

    // Файлы исходников proxy/ которые нужно скачать
    private static readonly string[] ProxySourceFiles =
    [
        "__init__.py", "balancer.py", "bridge.py", "config.py",
        "fake_tls.py", "raw_websocket.py", "stats.py", "tg_ws_proxy.py", "utils.py"
    ];
    private const string ProxyRawBase = "https://raw.githubusercontent.com/Flowseal/tg-ws-proxy/main/proxy/";

    // ── Состояние ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyRunning;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyInstalled;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isTgProxyDownloading;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDownloadStatus = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyVersion = "—";
    private Process? _tgProxyProcess;
    public System.Collections.ObjectModel.ObservableCollection<string> TgProxyLogs { get; } = new();

    // ── Настройки ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyHost = "127.0.0.1";
    partial void OnTgProxyHostChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyPort = "1443";
    partial void OnTgProxyPortChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxySecret = "";
    partial void OnTgProxySecretChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDomain = "";
    partial void OnTgProxyDomainChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyVerbose = false;
    partial void OnTgProxyVerboseChanged(bool value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyPreferIPv4 = true;
    partial void OnTgProxyPreferIPv4Changed(bool value) => SaveSettings();

    // ── Текст кнопки запуска ──
    public string TgProxyToggleText => TgProxyRunning ? "⏹ Остановить прокси" : "▶ Запустить прокси";
    partial void OnTgProxyRunningChanged(bool value) => OnPropertyChanged(nameof(TgProxyToggleText));

    // ── Инициализация при первом входе на вкладку ──
    private bool _tgProxyTabVisited = false;

    public void OnTgProxyTabActivated()
    {
        if (_tgProxyTabVisited) return;
        _tgProxyTabVisited = true;

        TgProxyInstalled = File.Exists(PythonExe) && File.Exists(ProxyScript);
        if (!TgProxyInstalled)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CustomDialog.Show(
                        "🔧 TG WS Proxy",
                        "Компонент TG WS Proxy не установлен.\n\nБудет скачано:\n• Python Embeddable (~12 МБ)\n• Исходники прокси (~50 КБ)\n• Пакет cryptography\n\nЗагрузить сейчас?",
                        "Загрузить", "Отмена"))
                    {
                        _ = DownloadTgProxyAsync();
                    }
                });
            }
        }
        else
        {
            TgProxyVersion = GetTgProxyLocalVersion();
        }
    }

    // ── Установка ──
    [RelayCommand]
    private async Task DownloadTgProxyAsync()
    {
        IsTgProxyDownloading = true;
        AddTgProxyLog("⬇️ Начало установки TG WS Proxy...");

        try
        {
            Directory.CreateDirectory(TgProxyDir);
            Directory.CreateDirectory(ProxyScriptDir);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            http.Timeout = TimeSpan.FromMinutes(5);

            // Шаг 1: Python Embeddable
            if (!File.Exists(PythonExe))
            {
                TgProxyDownloadStatus = "⬇️ Скачиваем Python Embeddable...";
                AddTgProxyLog("📦 Скачиваем Python 3.11 Embeddable...");

                var pythonZipUrl = "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip";
                var zipBytes = await http.GetByteArrayAsync(pythonZipUrl);
                var zipPath = Path.Combine(TgProxyDir, "python_embed.zip");
                await File.WriteAllBytesAsync(zipPath, zipBytes);

                TgProxyDownloadStatus = "📦 Распаковываем Python...";
                Directory.CreateDirectory(PythonDir);
                ZipFile.ExtractToDirectory(zipPath, PythonDir, overwriteFiles: true);
                File.Delete(zipPath);

                // Правим python311._pth — это единственный способ добавить пути в embeddable Python
                // PYTHONPATH и sys.path игнорируются пока не включён site
                var pthFile = Directory.GetFiles(PythonDir, "python*._pth").FirstOrDefault();
                if (pthFile != null)
                {
                    var pthSitePackages = Path.Combine(PythonDir, "Lib", "site-packages");
                    var lines = new List<string>();
                    lines.Add(".");
                    lines.Add(pthSitePackages);
                    lines.Add(ProxyScriptDir);
                    lines.Add("import site");
                    File.WriteAllLines(pthFile, lines);
                }

                AddTgProxyLog("✅ Python распакован");
            }

            // Всегда обновляем .pth — на случай если пути изменились или установка неполная
            FixPythonPth();

            // Шаг 2: pip через get-pip.py
            var pipExe = Directory.GetFiles(PythonDir, "pip.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (pipExe is null)
            {
                TgProxyDownloadStatus = "⬇️ Устанавливаем pip...";
                AddTgProxyLog("📦 Устанавливаем pip...");

                var getPipBytes = await http.GetByteArrayAsync("https://bootstrap.pypa.io/get-pip.py");
                var getPipPath = Path.Combine(TgProxyDir, "get-pip.py");
                await File.WriteAllBytesAsync(getPipPath, getPipBytes);

                await RunProcessAsync(PythonExe, $"\"{getPipPath}\"", PythonDir,
                    extraEnv: GetPythonEnv());
                File.Delete(getPipPath);

                pipExe = Directory.GetFiles(PythonDir, "pip.exe", SearchOption.AllDirectories).FirstOrDefault()
                         ?? Path.Combine(PythonDir, "Scripts", "pip.exe");
                AddTgProxyLog("✅ pip установлен");
            }

            // Шаг 3: cryptography
            var sitePackages = Path.Combine(PythonDir, "Lib", "site-packages");
            var cryptoDir = Directory.Exists(sitePackages)
                ? Directory.GetDirectories(sitePackages, "cryptography*").FirstOrDefault()
                : null;
            if (cryptoDir is null)
            {
                TgProxyDownloadStatus = "📦 Устанавливаем cryptography...";
                AddTgProxyLog("📦 Устанавливаем cryptography...");
                await RunProcessAsync(pipExe!, "install cryptography --quiet --no-warn-script-location",
                    PythonDir, extraEnv: GetPythonEnv(), ignoreExitCode: true);
                AddTgProxyLog("✅ cryptography установлен");
            }

            // Шаг 4: исходники proxy/
            TgProxyDownloadStatus = "⬇️ Скачиваем исходники прокси...";
            AddTgProxyLog("📄 Скачиваем исходники proxy/...");

            // Получаем версию
            using var noRedirect = new HttpClientHandler { AllowAutoRedirect = false };
            using var verHttp = new HttpClient(noRedirect);
            verHttp.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var verResp = await verHttp.GetAsync("https://github.com/Flowseal/tg-ws-proxy/releases/latest");
            var tagName = verResp.Headers.Location?.ToString().Split('/').LastOrDefault() ?? "unknown";

            foreach (var file in ProxySourceFiles)
            {
                var url = ProxyRawBase + file;
                var dest = Path.Combine(ProxyScriptDir, file);
                var content = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(dest, content);
            }

            File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);
            AddTgProxyLog($"✅ Исходники proxy/ скачаны ({tagName})");

            TgProxyVersion = tagName;
            TgProxyInstalled = true;
            TgProxyDownloadStatus = $"✅ Установлено {tagName}";
            AddTgProxyLog("🎉 TG WS Proxy готов к работе!");

            if (string.IsNullOrWhiteSpace(TgProxySecret))
                GenerateTgProxySecret();

            // Если прокси запущен со старой сломанной установкой — перезапускаем
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
        }
        finally
        {
            IsTgProxyDownloading = false;
        }
    }

    private async Task RunProcessAsync(string exe, string args, string workDir,
        Dictionary<string, string>? extraEnv = null, bool ignoreExitCode = false)
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
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi) ?? throw new Exception($"Не удалось запустить {exe}");
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendTgLog(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        if (!ignoreExitCode && proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} завершился с кодом {proc.ExitCode}");
    }

    // Прописываем нужные пути в python311._pth — единственный способ управлять sys.path
    // в embeddable Python (PYTHONPATH там игнорируется без включённого site)
    private void FixPythonPth()
    {
        var pthFile = Directory.GetFiles(PythonDir, "python*._pth").FirstOrDefault();
        if (pthFile is null) return;

        var sitePackages = Path.Combine(PythonDir, "Lib", "site-packages");
        Directory.CreateDirectory(sitePackages);

        var lines = new List<string>
        {
            ".",
            "python311.zip",
            sitePackages,
            ProxyScriptDir,
            "import site"
        };
        File.WriteAllLines(pthFile, lines);
    }

    // Переменные окружения для корректной работы embeddable Python с пакетами
    private Dictionary<string, string> GetPythonEnv()
    {
        var sitePackages = Path.Combine(PythonDir, "Lib", "site-packages");
        var scripts = Path.Combine(PythonDir, "Scripts");
        return new Dictionary<string, string>
        {
            ["PYTHONHOME"] = PythonDir,
            ["PYTHONPATH"] = $"{ProxyScriptDir};{sitePackages}",
            ["PATH"] = $"{PythonDir};{scripts};{Environment.GetEnvironmentVariable("PATH")}"
        };
    }

    private string GetTgProxyLocalVersion()
    {
        var versionFile = Path.Combine(TgProxyDir, "version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
    }

    // ── Генерация Secret (32 hex = 16 байт) ──
    [RelayCommand]
    private void GenerateTgProxySecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        TgProxySecret = Convert.ToHexString(bytes).ToLowerInvariant();
        AddTgProxyLog("🔑 Secret сгенерирован");
    }

    // ── Запуск / Остановка ──
    [RelayCommand]
    private void ToggleTgProxy()
    {
        if (TgProxyRunning) StopTgProxy();
        else StartTgProxy();
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
            AddTgProxyLog("❌ Компонент не установлен. Нажмите «Обновления».");
            return;
        }

        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("❌ Secret не задан. Нажмите 🔄 для генерации.");
            return;
        }

        var scriptArgs = BuildArguments();
        // Запускаем: python.exe proxy/tg_ws_proxy.py <args>
        // -m не подходит т.к. нет пакета, запускаем скрипт напрямую
        var fullArgs = $"\"{ProxyScript}\" {scriptArgs}";
        AddTgProxyLog($"🔧 python {fullArgs}");

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
            AddTgProxyLog($"   Слушает: {TgProxyHost}:{TgProxyPort}");

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
        args.Append($"--host {TgProxyHost}");
        args.Append($" --port {TgProxyPort}");
        args.Append($" --secret {TgProxySecret}");

        var domain = TgProxyDomain.Trim();
        if (!string.IsNullOrWhiteSpace(domain))
            args.Append($" --fake-tls-domain {domain}");

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
        try { await proc.WaitForExitAsync(); }
        catch (Exception) { /* процесс удалён через StopTgProxy */ }

        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TgProxyRunning = false;
                int? code = null;
                try { code = proc.ExitCode; } catch (Exception) { /* disposed */ }
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
            using var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var response = await http.GetAsync("https://github.com/Flowseal/tg-ws-proxy/releases/latest");
            var latest = response.Headers.Location?.ToString().Split('/').LastOrDefault() ?? "?";
            var local = GetTgProxyLocalVersion();
            if (latest == local)
                AddTgProxyLog($"✅ Актуальная версия ({local})");
            else
            {
                AddTgProxyLog($"⬆️ Доступна версия {latest} (текущая {local})");
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    var update = Application.Current.Dispatcher.Invoke(() =>
                        CustomDialog.Show("🔄 Обновление", $"Доступна версия {latest}.\nОбновить исходники прокси?", "Обновить", "Отмена"));
                    if (update)
                    {
                        await UpdateProxySourcesAsync(latest);
                    }
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
        AddTgProxyLog($"⬇️ Обновляем исходники до {tagName}...");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");

            var rawBase = $"https://raw.githubusercontent.com/Flowseal/tg-ws-proxy/{tagName}/proxy/";
            foreach (var file in ProxySourceFiles)
            {
                var content = await http.GetByteArrayAsync(rawBase + file);
                await File.WriteAllBytesAsync(Path.Combine(ProxyScriptDir, file), content);
            }

            File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);
            TgProxyVersion = tagName;
            AddTgProxyLog($"✅ Исходники обновлены до {tagName}");
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка обновления: {ex.Message}");
        }
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

    private string TgDeepLink
    {
        get
        {
            var domain = TgProxyDomain.Trim();
            if (!string.IsNullOrWhiteSpace(domain))
            {
                // ee-secret: ee + 32hex + hex(domain)
                var domainHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(domain)).ToLowerInvariant();
                return $"tg://proxy?server={domain}&port={TgProxyPort}&secret=ee{TgProxySecret}{domainHex}";
            }
            // dd-secret: dd + 32hex
            return $"tg://proxy?server=127.0.0.1&port={TgProxyPort}&secret=dd{TgProxySecret}";
        }
    }

    [RelayCommand]
    private void OpenInTelegram()
    {
        if (string.IsNullOrWhiteSpace(TgProxySecret)) { AddTgProxyLog("Secret not set."); return; }
        try
        {
            Process.Start(new ProcessStartInfo(TgDeepLink) { UseShellExecute = true });
            AddTgProxyLog("Opening Telegram with proxy settings...");
        }
        catch (Exception ex) { AddTgProxyLog($"Error: {ex.Message}"); }
    }

    [RelayCommand]
    private void CopyTgLink()
    {
        if (string.IsNullOrWhiteSpace(TgProxySecret)) { AddTgProxyLog("Secret not set."); return; }
        System.Windows.Clipboard.SetText(TgDeepLink);
        AddTgProxyLog($"Copied: {TgDeepLink}");
    }
}
