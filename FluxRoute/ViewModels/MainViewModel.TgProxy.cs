using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    // ── Пути ──
    private string TgProxyDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tg-proxy");
    private string TgProxyExe => Path.Combine(TgProxyDir, "tg-ws-proxy.exe");
    private string TgProxyConfigPath => Path.Combine(TgProxyDir, "config.json");

    // ── Состояние ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyRunning;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyInstalled;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isTgProxyDownloading;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDownloadStatus = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyVersion = "—";
    private Process? _tgProxyProcess;
    private CancellationTokenSource? _tgProxyHideCts;
    public System.Collections.ObjectModel.ObservableCollection<string> TgProxyLogs { get; } = new();

    // ── Настройки MTProto ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyHost = "127.0.0.1";
    partial void OnTgProxyHostChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyPort = "1080";
    partial void OnTgProxyPortChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxySecret = "";
    partial void OnTgProxySecretChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDatacenters = "2:149.154.167.220\n4:149.154.167.220";
    partial void OnTgProxyDatacentersChanged(string value) => SaveSettings();

    // ── Cloudflare ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyCloudflarEnabled = true;
    partial void OnTgProxyCloudflarEnabledChanged(bool value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyCloudflarePriority = true;
    partial void OnTgProxyCloudflarePriorityChanged(bool value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyUseCustomDomain = false;
    partial void OnTgProxyUseCustomDomainChanged(bool value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyCustomDomain = "";
    partial void OnTgProxyCustomDomainChanged(string value) => SaveSettings();

    // ── Логи и производительность ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyVerbose = false;
    partial void OnTgProxyVerboseChanged(bool value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyBufferKb = "256";
    partial void OnTgProxyBufferKbChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyWsPool = "4";
    partial void OnTgProxyWsPoolChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyMaxLogMb = "5.0";
    partial void OnTgProxyMaxLogMbChanged(string value) => SaveSettings();

    // ── Обновления ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyCheckUpdatesOnStart = true;
    partial void OnTgProxyCheckUpdatesOnStartChanged(bool value) => SaveSettings();

    // ── Текст кнопки запуска ──
    public string TgProxyToggleText => TgProxyRunning ? "⏹ Остановить прокси" : "▶ Запустить прокси";
    partial void OnTgProxyRunningChanged(bool value) => OnPropertyChanged(nameof(TgProxyToggleText));

    // ── Инициализация при первом входе на вкладку ──
    private bool _tgProxyTabVisited = false;

    public void OnTgProxyTabActivated()
    {
        if (_tgProxyTabVisited) return;
        _tgProxyTabVisited = true;

        TgProxyInstalled = File.Exists(TgProxyExe);
        if (!TgProxyInstalled)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CustomDialog.Show(
                        "🔧 TG WS Proxy",
                        "Компонент TG WS Proxy не установлен.\n\nЗагрузить его сейчас с GitHub (~5 МБ)?",
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

    // ── Скачивание ──
    private async Task DownloadTgProxyAsync()
    {
        IsTgProxyDownloading = true;
        TgProxyDownloadStatus = "🔍 Поиск последней версии...";
        AddTgProxyLog("⬇️ Начало загрузки TG WS Proxy...");

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");

            var json = await http.GetStringAsync("https://api.github.com/repos/Flowseal/tg-ws-proxy/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "unknown";
            string? downloadUrl = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals("TgWsProxy_windows.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (downloadUrl is null)
            {
                TgProxyDownloadStatus = "❌ Не найден Windows-релиз";
                AddTgProxyLog("❌ Не найден TgWsProxy_windows.exe в релизе");
                return;
            }

            TgProxyDownloadStatus = $"⬇️ Загружаем {tagName}...";
            Directory.CreateDirectory(TgProxyDir);

            var bytes = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(TgProxyExe, bytes);

            // Сохраняем версию
            File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);

            TgProxyVersion = tagName;
            TgProxyInstalled = true;
            TgProxyDownloadStatus = $"✅ Установлено {tagName}";
            AddTgProxyLog($"✅ TG WS Proxy {tagName} успешно установлен");
        }
        catch (Exception ex)
        {
            TgProxyDownloadStatus = $"❌ Ошибка: {ex.Message}";
            AddTgProxyLog($"❌ Ошибка загрузки: {ex.Message}");
        }
        finally
        {
            IsTgProxyDownloading = false;
        }
    }

    private string GetTgProxyLocalVersion()
    {
        var versionFile = Path.Combine(TgProxyDir, "version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
    }

    // ── Генерация Secret ──
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
        if (!File.Exists(TgProxyExe))
        {
            AddTgProxyLog("❌ tg-ws-proxy.exe не найден. Установите компонент.");
            return;
        }

        WriteTgProxyConfig();

        // Запускаем через CreateProcess с SW_HIDE, чтобы окно не мелькало с самого старта
        var psi = new ProcessStartInfo
        {
            FileName = TgProxyExe,
            Arguments = $"--config \"{TgProxyConfigPath}\"",
            WorkingDirectory = TgProxyDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        try
        {
            _tgProxyProcess = Process.Start(psi);
            if (_tgProxyProcess is null)
            {
                AddTgProxyLog("❌ Не удалось запустить процесс");
                return;
            }

            var pid = (uint)_tgProxyProcess.Id;

            // Регистрируем PID — WinEventHook скроет окно при EVENT_OBJECT_SHOW
            _trackedPids = new HashSet<uint>(_trackedPids) { pid };
            InstallWindowHook();

            // Агрессивно скрываем все окна процесса в первые 5 секунд
            _tgProxyHideCts?.Cancel();
            _tgProxyHideCts = new CancellationTokenSource();
            _ = AggressiveHideLoopAsync(pid, _tgProxyHideCts.Token);

            TgProxyRunning = true;
            AddTgProxyLog($"▶ TG WS Proxy запущен (PID {_tgProxyProcess.Id})");
            AddTgProxyLog($"   MTProto: {TgProxyHost}:{TgProxyPort}");

            _ = WatchTgProxyProcessAsync(_tgProxyProcess);
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка запуска: {ex.Message}");
        }
    }

    // Каждые 200 мс прячем все видимые окна процесса (включая трей-попапы)
    private async Task AggressiveHideLoopAsync(uint pid, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            HideAllWindowsOfPid(pid);
        }
        // После 10 секунд продолжаем реже — раз в секунду, пока процесс жив
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            HideAllWindowsOfPid(pid);
        }
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows_TG(EnumWindowsProc_TG lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId_TG(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible_TG(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow_TG(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    private delegate bool EnumWindowsProc_TG(IntPtr hWnd, IntPtr lParam);

    private static void HideAllWindowsOfPid(uint pid)
    {
        EnumWindows_TG((hWnd, _) =>
        {
            GetWindowThreadProcessId_TG(hWnd, out uint winPid);
            if (winPid == pid)
                ShowWindow_TG(hWnd, 0); // SW_HIDE
            return true;
        }, IntPtr.Zero);
    }

    private async Task WatchTgProxyProcessAsync(Process proc)
    {
        await proc.WaitForExitAsync();
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TgProxyRunning = false;
                AddTgProxyLog("⏹ TG WS Proxy остановлен");
            });
        }
    }

    private void StopTgProxy()
    {
        _tgProxyHideCts?.Cancel();
        _tgProxyHideCts = null;

        try
        {
            if (_tgProxyProcess is { HasExited: false })
            {
                var pid = (uint)_tgProxyProcess.Id;
                _tgProxyProcess.Kill(entireProcessTree: true);
                _tgProxyProcess.Dispose();
                _tgProxyProcess = null;

                var updated = new HashSet<uint>(_trackedPids);
                updated.Remove(pid);
                _trackedPids = updated;

                // Снимаем хук если winws тоже не работает
                if (!IsRunning)
                    RemoveWindowHook();
            }
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"⚠ Ошибка остановки: {ex.Message}");
        }
        TgProxyRunning = false;
        AddTgProxyLog("⏹ TG WS Proxy остановлен");
    }

    // ── Конфиг ──
    private void WriteTgProxyConfig()
    {
        Directory.CreateDirectory(TgProxyDir);

        // Парсим датацентры
        var dcList = new List<DcEntry>();
        foreach (var line in TgProxyDatacenters.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out var dcId))
                dcList.Add(new DcEntry { Dc = dcId, Ip = parts[1].Trim() });
        }

        var cfg = new TgProxyConfig
        {
            Host = TgProxyHost,
            Port = int.TryParse(TgProxyPort, out var p) ? p : 1080,
            Secret = TgProxySecret,
            Datacenters = dcList,
            CloudflareEnabled = TgProxyCloudflarEnabled,
            CloudflarePriority = TgProxyCloudflarePriority,
            CustomDomain = TgProxyUseCustomDomain ? TgProxyCustomDomain : "",
            Verbose = TgProxyVerbose,
            BufferKb = int.TryParse(TgProxyBufferKb, out var buf) ? buf : 256,
            WsPool = int.TryParse(TgProxyWsPool, out var pool) ? pool : 4,
            MaxLogMb = double.TryParse(TgProxyMaxLogMb, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var logMb) ? logMb : 5.0,
            CheckUpdates = TgProxyCheckUpdatesOnStart
        };

        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        File.WriteAllText(TgProxyConfigPath, json);
        AddTgProxyLog("💾 Конфиг сохранён");
    }

    [RelayCommand]
    private void SaveTgProxyConfig()
    {
        WriteTgProxyConfig();
        AddTgProxyLog("✅ Настройки применены");
    }

    [RelayCommand]
    private async Task CheckTgProxyUpdates()
    {
        AddTgProxyLog("🔍 Проверяем обновления TG WS Proxy...");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var json = await http.GetStringAsync("https://api.github.com/repos/Flowseal/tg-ws-proxy/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var latest = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
            var local = GetTgProxyLocalVersion();
            if (latest == local)
                AddTgProxyLog($"✅ Актуальная версия ({local})");
            else
                AddTgProxyLog($"⬆️ Доступна версия {latest} (текущая {local})");
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка проверки: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearTgProxyLogs() => TgProxyLogs.Clear();

    private void AddTgProxyLog(string msg)
    {
        TgProxyLogs.Add(msg);
        while (TgProxyLogs.Count > 200)
            TgProxyLogs.RemoveAt(0);
    }

    public void StopTgProxyOnExit() => StopTgProxy();

    // ── JSON-модели для config.json ──
    private sealed class TgProxyConfig
    {
        [JsonPropertyName("host")] public string Host { get; set; } = "127.0.0.1";
        [JsonPropertyName("port")] public int Port { get; set; } = 1080;
        [JsonPropertyName("secret")] public string Secret { get; set; } = "";
        [JsonPropertyName("datacenters")] public List<DcEntry> Datacenters { get; set; } = new();
        [JsonPropertyName("cloudflare_enabled")] public bool CloudflareEnabled { get; set; } = true;
        [JsonPropertyName("cloudflare_priority")] public bool CloudflarePriority { get; set; } = true;
        [JsonPropertyName("custom_domain")] public string? CustomDomain { get; set; }
        [JsonPropertyName("verbose")] public bool Verbose { get; set; }
        [JsonPropertyName("buffer_kb")] public int BufferKb { get; set; } = 256;
        [JsonPropertyName("ws_pool")] public int WsPool { get; set; } = 4;
        [JsonPropertyName("max_log_mb")] public double MaxLogMb { get; set; } = 5.0;
        [JsonPropertyName("check_updates")] public bool CheckUpdates { get; set; } = true;
    }

    private sealed class DcEntry
    {
        [JsonPropertyName("dc")] public int Dc { get; set; }
        [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    }
}
