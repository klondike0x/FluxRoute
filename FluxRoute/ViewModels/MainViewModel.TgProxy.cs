using CommunityToolkit.Mvvm.Input;
using FluxRoute.Services;
using FluxRoute.Views;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using Application = System.Windows.Application;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private TgWsProxyServer? _tgWsProxy;
    private bool _tgProxyTabVisited;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyRunning;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyInstalled;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool isTgProxyDownloading;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDownloadStatus = "";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyVersion = "C# встроенный";

    public ObservableCollection<string> TgProxyLogs { get; } = new();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyHost = "127.0.0.1";
    partial void OnTgProxyHostChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyPort = "1443";
    partial void OnTgProxyPortChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxySecret = "";
    partial void OnTgProxySecretChanged(string value) => SaveSettings();

    // Kept for compatibility with existing settings. Native mode uses the configured
    // SNI/Cloudflare domain instead of launching a Python script.
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDomain = "";
    partial void OnTgProxyDomainChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyVerbose;
    partial void OnTgProxyVerboseChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyPreferIPv4 = true;
    partial void OnTgProxyPreferIPv4Changed(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyAutoStartOnAppLaunch = true;
    partial void OnTgProxyAutoStartOnAppLaunchChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyDcIps = "2:149.154.167.220\n4:149.154.167.220";
    partial void OnTgProxyDcIpsChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfEnabled = true;
    partial void OnTgProxyCfEnabledChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfPriority = true;
    partial void OnTgProxyCfPriorityChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool tgProxyCfDomainEnabled;
    partial void OnTgProxyCfDomainEnabledChanged(bool value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyCfDomain = "";
    partial void OnTgProxyCfDomainChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyCfWorkerDomains = "";
    partial void OnTgProxyCfWorkerDomainsChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyBufKb = "256";
    partial void OnTgProxyBufKbChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyPoolSize = "4";
    partial void OnTgProxyPoolSizeChanged(string value) => SaveSettings();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string tgProxyLogMaxMb = "5.0";
    partial void OnTgProxyLogMaxMbChanged(string value) => SaveSettings();

    public string TgProxyToggleText => TgProxyRunning ? "⏹ Остановить прокси" : "▶ Запустить прокси";

    partial void OnTgProxyRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(TgProxyToggleText));
        OnPropertyChanged(nameof(ActiveServicesCount));
        OnPropertyChanged(nameof(ActiveServicesSummary));
    }

    public void OnTgProxyTabActivated()
    {
        if (_tgProxyTabVisited) return;
        _tgProxyTabVisited = true;
        EnsureTgProxyStateInitialized();
    }

    public void InitializeTgProxyOnStartup()
    {
        EnsureTgProxyStateInitialized();
        if (!TgProxyAutoStartOnAppLaunch || TgProxyRunning) return;
        if (string.IsNullOrWhiteSpace(TgProxySecret))
        {
            AddTgProxyLog("⏭ Автозапуск TG WS Proxy пропущен: secret не задан.");
            return;
        }
        StartTgProxy();
    }

    private void EnsureTgProxyStateInitialized()
    {
        // The proxy is part of FluxRoute now; no Python files or download are required.
        TgProxyInstalled = true;
        TgProxyVersion = "C# встроенный";
        if (string.IsNullOrWhiteSpace(TgProxySecret))
            GenerateTgProxySecret(silent: true);
    }

    [RelayCommand]
    private Task DownloadTgProxyAsync()
    {
        EnsureTgProxyStateInitialized();
        TgProxyDownloadStatus = "✅ TG WS Proxy уже встроен в FluxRoute";
        AddTgProxyLog("ℹ️ TG WS Proxy работает на встроенной C#-реализации — Python не требуется.");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void GenerateTgProxySecret()
    {
        GenerateTgProxySecret(silent: false);
    }

    private void GenerateTgProxySecret(bool silent)
    {
        TgProxySecret = "dd" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        if (!silent) AddTgProxyLog("🔑 Secret сгенерирован");
    }

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
            AddTgProxyLog("⏳ Идёт подготовка прокси, подождите завершения...");
            return;
        }

        if (!TryParseSecret(TgProxySecret, out byte[] secret))
        {
            AddTgProxyLog("❌ Некорректный Secret: требуется 32 hex-символа.");
            return;
        }

        if (!int.TryParse(TgProxyPort, out int port) || port is < 1 or > 65535)
        {
            AddTgProxyLog("❌ Некорректный порт TG WS Proxy.");
            return;
        }

        var targets = ParseDataCenterTargets(TgProxyDcIps);
        int bufferSize = int.TryParse(TgProxyBufKb, out int kb) && kb > 0
            ? Math.Clamp(kb, 32, 4096) * 1024
            : 256 * 1024;

        var server = new TgWsProxyServer();
        server.Log += AppendTgLog;
        try
        {
            server.Start(new TgWsProxyOptions
            {
                ListenHost = string.IsNullOrWhiteSpace(TgProxyHost) ? "127.0.0.1" : TgProxyHost.Trim(),
                ListenPort = port,
                Secret = secret,
                DataCenterTargets = targets,
                CloudflareEnabled = TgProxyCfEnabled,
                CloudflarePriority = TgProxyCfPriority,
                CloudflareDomainEnabled = TgProxyCfDomainEnabled,
                CloudflareDomain = TgProxyCfDomain.Trim(),
                CloudflareWorkerDomains = ParseDomainList(TgProxyCfWorkerDomains),
                BufferSize = bufferSize,
                Verbose = TgProxyVerbose
            });
            _tgWsProxy = server;
            TgProxyRunning = true;
            AddTgProxyLog("▶ TG WS Proxy запущен (встроенный C#)");
            AddTgProxyLog($"Слушает: {TgProxyHost}:{port}");
        }
        catch (Exception ex)
        {
            server.Dispose();
            AddTgProxyLog($"❌ Ошибка запуска TG WS Proxy: {ex.Message}");
        }
    }

    private void StopTgProxy()
    {
        try { _tgWsProxy?.Dispose(); }
        catch (Exception ex) { AddTgProxyLog($"⚠ Ошибка остановки: {ex.Message}"); }
        finally
        {
            _tgWsProxy = null;
            TgProxyRunning = false;
        }
    }

    [RelayCommand]
    private Task CheckTgProxyUpdates()
    {
        TgProxyDownloadStatus = "ℹ️ Встроенная C#-реализация обновляется вместе с FluxRoute";
        AddTgProxyLog("ℹ️ Отдельное обновление TG WS Proxy не требуется: реализация встроена в приложение.");
        return Task.CompletedTask;
    }

    private void ClearTgProxyLogs() => TgProxyLogs.Clear();

    private void AddTgProxyLog(string message)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => AddTgProxyLog(message));
            return;
        }

        TgProxyLogs.Add(message);
        while (TgProxyLogs.Count > 500)
            TgProxyLogs.RemoveAt(0);
    }

    private void AppendTgLog(string message) => AddTgProxyLog(message);

    public void StopTgProxyOnExit() => StopTgProxy();

    private string TgDeepLink =>
        $"tg://proxy?server={Uri.EscapeDataString(TgProxyHost)}&port={TgProxyPort}&secret={TgProxySecret}";

    [RelayCommand]
    private void OpenInTelegram()
    {
        if (!TryParseSecret(TgProxySecret, out _))
        {
            AddTgProxyLog("❌ Secret не задан.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TgDeepLink)
            {
                UseShellExecute = true
            });
            AddTgProxyLog("🔗 Открыта ссылка настройки прокси в Telegram.");
        }
        catch (Exception ex) { AddTgProxyLog($"❌ Не удалось открыть Telegram: {ex.Message}"); }
    }

    [RelayCommand]
    private void CopyTgProxyLink()
    {
        if (!TryParseSecret(TgProxySecret, out _))
        {
            AddTgProxyLog("❌ Secret не задан.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(TgDeepLink);
            AddTgProxyLog("📋 Ссылка TG Proxy скопирована.");
        }
        catch (Exception ex) { AddTgProxyLog($"❌ Не удалось скопировать ссылку: {ex.Message}"); }
    }

    private static bool TryParseSecret(string value, out byte[] secret)
    {
        secret = Array.Empty<byte>();
        string raw = value?.Trim() ?? string.Empty;
        if (raw.StartsWith("dd", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ee", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..];
        if (raw.Length != 32) return false;
        try
        {
            secret = Convert.FromHexString(raw);
            return secret.Length == 16;
        }
        catch (FormatException) { return false; }
    }

    private static IReadOnlyList<string> ParseDomainList(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(domain => domain.Contains('.', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private static IReadOnlyDictionary<int, string> ParseDataCenterTargets(string value)
    {
        var result = new Dictionary<int, string>();
        foreach (string line in (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || separator == line.Length - 1) continue;
            if (int.TryParse(line[..separator].Trim(), out int dc))
                result[dc] = line[(separator + 1)..].Trim();
        }
        return result;
    }
}