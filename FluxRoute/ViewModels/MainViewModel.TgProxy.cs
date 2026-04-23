using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    // ── Пути ──
    private string TgProxyDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tg-proxy");
    private string TgProxyExe => Path.Combine(TgProxyDir, "mtg.exe");
    private string TgProxyConfigPath => Path.Combine(TgProxyDir, "config.toml");

    // ── Состояние ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyRunning;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool tgProxyInstalled;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isTgProxyDownloading;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDownloadStatus = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyVersion = "—";
    private Process? _tgProxyProcess;
    public System.Collections.ObjectModel.ObservableCollection<string> TgProxyLogs { get; } = new();

    // ── Настройки ──
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyHost = "0.0.0.0";
    partial void OnTgProxyHostChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyPort = "3128";
    partial void OnTgProxyPortChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxySecret = "";
    partial void OnTgProxySecretChanged(string value) => SaveSettings();
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string tgProxyDomain = "www.google.com";
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

        TgProxyInstalled = File.Exists(TgProxyExe);
        if (!TgProxyInstalled)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (CustomDialog.Show(
                        "🔧 MTG Proxy",
                        "Компонент MTG (headless MTProto прокси) не установлен.\n\nЗагрузить его сейчас с GitHub (~5 МБ)?",
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
    [RelayCommand]
    private async Task DownloadTgProxyAsync()
    {
        IsTgProxyDownloading = true;
        TgProxyDownloadStatus = "🔍 Поиск последней версии...";
        AddTgProxyLog("⬇️ Начало загрузки MTG...");

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");

            var json = await http.GetStringAsync("https://api.github.com/repos/9seconds/mtg/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "unknown";
            string? downloadUrl = null;
            string? assetName = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                // Ищем Windows AMD64: mtg_X.X.X_windows_amd64.zip или mtg_windows_amd64.zip
                if (name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("amd64", StringComparison.OrdinalIgnoreCase) &&
                    (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                    break;
                }
            }

            if (downloadUrl is null)
            {
                TgProxyDownloadStatus = "❌ Не найден Windows-релиз";
                AddTgProxyLog("❌ Не найден mtg windows amd64 в релизе");
                return;
            }

            TgProxyDownloadStatus = $"⬇️ Загружаем {tagName}...";
            Directory.CreateDirectory(TgProxyDir);

            var bytes = await http.GetByteArrayAsync(downloadUrl);

            // Удаляем старый exe если есть (не заблокирован)
            if (File.Exists(TgProxyExe))
                File.Delete(TgProxyExe);

            if (assetName!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var zipPath = Path.Combine(TgProxyDir, "mtg.zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                await File.WriteAllBytesAsync(zipPath, bytes);

                // Закрываем архив до удаления zip
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals("mtg.exe", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Equals("mtg", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    if (entry is not null)
                        entry.ExtractToFile(TgProxyExe, overwrite: true);
                    else
                        AddTgProxyLog("⚠ Не найден исполняемый файл в архиве");
                }
                File.Delete(zipPath);
            }
            else
            {
                await File.WriteAllBytesAsync(TgProxyExe, bytes);
            }

            File.WriteAllText(Path.Combine(TgProxyDir, "version.txt"), tagName);

            TgProxyVersion = tagName;
            TgProxyInstalled = true;
            TgProxyDownloadStatus = $"✅ Установлено {tagName}";
            AddTgProxyLog($"✅ MTG {tagName} успешно установлен");
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
        // mtg fake-tls secret: ee + 32 hex + encoded domain
        var bytes = RandomNumberGenerator.GetBytes(16);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        var domain = TgProxyDomain.Trim();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var domainHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(domain)).ToLowerInvariant();
            TgProxySecret = "ee" + hex + domainHex;
            AddTgProxyLog($"🔑 Fake-TLS secret сгенерирован (домен: {domain})");
        }
        else
        {
            TgProxySecret = hex;
            AddTgProxyLog("🔑 Simple secret сгенерирован");
        }
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
            AddTgProxyLog("❌ mtg.exe не найден. Установите компонент.");
            return;
        }

        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("❌ Secret не задан. Нажмите 🔄 для генерации.");
            return;
        }

        WriteMtgConfig();

        var psi = new ProcessStartInfo
        {
            FileName = TgProxyExe,
            // mtg v2: mtg run <config.toml>
            Arguments = $"run \"{TgProxyConfigPath}\"",
            WorkingDirectory = TgProxyDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

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
            AddTgProxyLog($"▶ MTG запущен (PID {_tgProxyProcess.Id})");
            AddTgProxyLog($"   Слушает: {TgProxyHost}:{TgProxyPort}");

            _ = WatchTgProxyProcessAsync(_tgProxyProcess);
        }
        catch (Exception ex)
        {
            AddTgProxyLog($"❌ Ошибка запуска: {ex.Message}");
        }
    }

    // Генерация TOML конфига для mtg v2
    private void WriteMtgConfig()
    {
        Directory.CreateDirectory(TgProxyDir);

        var bindAddr = $"{TgProxyHost}:{TgProxyPort}";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"secret = \"{TgProxySecret}\"");
        sb.AppendLine($"bind-to = \"{bindAddr}\"");
        if (TgProxyVerbose)
            sb.AppendLine("debug = true");
        if (TgProxyPreferIPv4)
            sb.AppendLine("prefer-ip = \"prefer-ipv4\"");

        File.WriteAllText(TgProxyConfigPath, sb.ToString());
        AddTgProxyLog("💾 config.toml записан");
    }

    private void AppendTgLog(string line)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            Application.Current.Dispatcher.Invoke(() => AddTgProxyLog(line));
    }

    private async Task WatchTgProxyProcessAsync(Process proc)
    {
        await proc.WaitForExitAsync();
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TgProxyRunning = false;
                AddTgProxyLog($"⏹ MTG остановлен (код: {proc.ExitCode})");
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
        AddTgProxyLog("⏹ MTG остановлен");
    }

    [RelayCommand]
    private async Task CheckTgProxyUpdates()
    {
        AddTgProxyLog("🔍 Проверяем обновления MTG...");
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var json = await http.GetStringAsync("https://api.github.com/repos/9seconds/mtg/releases/latest");
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
        while (TgProxyLogs.Count > 500)
            TgProxyLogs.RemoveAt(0);
    }

    public void StopTgProxyOnExit() => StopTgProxy();
}
