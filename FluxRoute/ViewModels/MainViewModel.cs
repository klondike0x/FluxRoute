using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Коллекции ──
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public ObservableCollection<string> OrchestratorLogs { get; } = new();
    public ObservableCollection<ProfileScore> ProfileScores { get; } = new();
    public ObservableCollection<string> RecentLogs { get; } = new();
    public ObservableCollection<string> UpdateLogs { get; } = new();
    public ObservableCollection<string> ServiceLogs { get; } = new();

    // ── События ──
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenAboutRequested;
    public event EventHandler<string>? ProfileSwitchNotification;

    // ── Профиль ──
    public string SelectedScriptName => SelectedProfile?.FileName ?? "—";
    [ObservableProperty] private ProfileItem? selectedProfile;
    partial void OnSelectedProfileChanged(ProfileItem? oldValue, ProfileItem? newValue)
    {
        if (!_suppressProfileWarning && _settingsLoaded && ShowProfileSwitchWarning
            && oldValue is not null && newValue is not null)
        {
            if (!CustomDialog.Show(
                "⚠️ Смена профиля",
                "Изменение профиля может повлиять на работу приложения и сетевые подключения. Продолжить?",
                "Продолжить", "Отмена", isDanger: true))
            {
                _suppressProfileWarning = true;
                SelectedProfile = oldValue;
                _suppressProfileWarning = false;
                return;
            }
        }

        OnPropertyChanged(nameof(SelectedScriptName));
        RunningScriptName = newValue?.FileName ?? "—";
        SaveSettings();

        if (!_suppressProfileWarning && _settingsLoaded && IsRunning && newValue is not null)
        {
            Stop();
            Start();
        }
    }

    // ── Статус ──
    [ObservableProperty] private string statusText = "Не запущено";
    [ObservableProperty] private string updateText = "Обновления не проверялись";
    [ObservableProperty] private string runningScriptName = "—";
    [ObservableProperty] private string pidText = "—";
    [ObservableProperty] private string uptimeText = "—";
    public string AppVersion { get; } = GetAppVersion();

    // ── Навигация ──
    [ObservableProperty] private int selectedTabIndex = 0;
    public string SelectedTabName => SelectedTabIndex switch
    {
        0 => "ГЛАВНАЯ",
        1 => "TG ПРОКСИ",
        2 => "ОРКЕСТРАТОР",
        3 => "ОБНОВЛЕНИЕ",
        4 => "ДИАГНОСТИКА",
        5 => "СЕРВИС",
        6 => "О ПРОГРАММЕ",
        _ => ""
    };
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedTabName));
        if (value == 1) OnTgProxyTabActivated();
    }

    // ── Боковая панель ──
    [ObservableProperty] private bool isSidebarExpanded = true;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    // ── Компактный интерфейс ──
    [ObservableProperty] private bool isSettingsOpen = false;
    [ObservableProperty] private bool isRunning = false;
    [ObservableProperty] private bool isLogsVisible = false;
    [ObservableProperty] private string currentStrategy = "—";
    [ObservableProperty] private string uploadSpeed = "0.0";
    [ObservableProperty] private string downloadSpeed = "0.0";
    [ObservableProperty] private string lastStatusMessage = "Готово";

    public string MainActionButtonText => IsRunning ? "⏹ Остановить" : "▶ Запустить";
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(MainActionButtonText));

    // ── Диагностика ──
    [ObservableProperty] private bool isAdmin;
    public string AdminText => IsAdmin ? "✅ Да" : "❌ Нет";
    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(AdminText));
    [ObservableProperty] private bool engineOk;
    public string EngineText => EngineOk ? "✅ Найдено" : "❌ Не найдено";
    partial void OnEngineOkChanged(bool value) => OnPropertyChanged(nameof(EngineText));
    [ObservableProperty] private bool winwsOk;
    public string WinwsText => WinwsOk ? "✅ Найдено" : "❌ Не найдено";
    partial void OnWinwsOkChanged(bool value) => OnPropertyChanged(nameof(WinwsText));
    [ObservableProperty] private bool winDivertDllOk;
    public string WinDivertDllText => WinDivertDllOk ? "✅ Найдено" : "❌ Не найдено";
    partial void OnWinDivertDllOkChanged(bool value) => OnPropertyChanged(nameof(WinDivertDllText));
    [ObservableProperty] private bool winDivertDriverOk;
    public string WinDivertDriverText => WinDivertDriverOk ? "✅ Найдено" : "❌ Не найдено";
    partial void OnWinDivertDriverOkChanged(bool value) => OnPropertyChanged(nameof(WinDivertDriverText));

    // ── Оркестратор ──
    [ObservableProperty] private bool orchestratorRunning;
    [ObservableProperty] private string orchestratorStatus = "Не запущен";
    [ObservableProperty] private string orchestratorNextCheck = "—";
    [ObservableProperty] private string orchestratorInterval = "1";
    partial void OnOrchestratorIntervalChanged(string value) => SaveSettings();
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string scanProgressText = "";
    public string OrchestratorToggleLabel => OrchestratorRunning ? "Остановить оркестратор" : "Запустить оркестратор";
    partial void OnOrchestratorRunningChanged(bool value) => OnPropertyChanged(nameof(OrchestratorToggleLabel));

    // ── Настройки сайтов ──
    [ObservableProperty] private bool siteYouTube = true;
    partial void OnSiteYouTubeChanged(bool value) => SaveSettings();
    [ObservableProperty] private bool siteDiscord = true;
    partial void OnSiteDiscordChanged(bool value) => SaveSettings();
    [ObservableProperty] private bool siteGoogle = true;
    partial void OnSiteGoogleChanged(bool value) => SaveSettings();
    [ObservableProperty] private bool siteTwitch = true;
    partial void OnSiteTwitchChanged(bool value) => SaveSettings();
    [ObservableProperty] private bool siteInstagram = true;
    partial void OnSiteInstagramChanged(bool value) => SaveSettings();
    [ObservableProperty] private bool siteTelegram = true;
    partial void OnSiteTelegramChanged(bool value) => SaveSettings();

    private readonly OrchestratorService _orchestrator;
    private readonly DispatcherTimer _orchestratorUiTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1) };

    // ── Сервис ──
    [ObservableProperty] private bool gameFilterEnabled;
    [ObservableProperty] private string gameFilterProtocol = "TCP и UDP";
    partial void OnGameFilterProtocolChanged(string value)
    {
        SaveSettings();
        if (GameFilterEnabled && _settingsLoaded)
        {
            try
            {
                var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
                Directory.CreateDirectory(utilsDir);
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(value));
                AddServiceLog($"🎮 Game Filter протокол изменён на {value}");
            }
            catch (Exception ex)
            {
                AddServiceLog($"❌ Ошибка обновления Game Filter: {ex.Message}");
            }
        }
    }
    public List<string> GameFilterProtocols { get; } = ["TCP и UDP", "TCP", "UDP"];

    [ObservableProperty] private string ipSetMode = "—";
    [ObservableProperty] private string zapretServiceStatus = "—";
    [ObservableProperty] private bool isServiceBusy;

    private string GameFilterFlagPath => Path.Combine(EngineDir, "utils", "game_filter.enabled");
    private string IpSetFilePath => Path.Combine(EngineDir, "lists", "ipset-all.txt");
    private string IpSetBackupPath => Path.Combine(EngineDir, "lists", "ipset-all.txt.backup");

    // ── Runtime ──
    private DateTimeOffset? _runStartedAt;
    private readonly DispatcherTimer _uptimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string EngineBinDir => Path.Combine(EngineDir, "bin");
    private string WinwsPath => Path.Combine(EngineBinDir, "winws.exe");
    private string WinDivertDllPath => Path.Combine(EngineBinDir, "WinDivert.dll");
    private string WinDivertSys64Path => Path.Combine(EngineBinDir, "WinDivert64.sys");
    private string WinDivertSysPath => Path.Combine(EngineBinDir, "WinDivert.sys");
    private string TargetsPath => Path.Combine(EngineDir, "utils", "targets.txt");
    private Process? _runningProcess;
    private CancellationTokenSource? _hideWindowsCts;
    private volatile HashSet<uint> _trackedPids = [];
    private IntPtr _winEventHook;
    private WinEventProc? _winEventCallback;
    private string EngineDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");

    private readonly UpdaterService _updater = new();
    private readonly SettingsService _settingsService = new();
    private bool _settingsLoaded = false;

    // ── Обновления ──
    [ObservableProperty] private bool autoUpdateEnabled = false;
    partial void OnAutoUpdateEnabledChanged(bool value) => SaveSettings();

    // ── Предупреждение при смене профиля ──
    [ObservableProperty] private bool showProfileSwitchWarning = true;
    partial void OnShowProfileSwitchWarningChanged(bool value) => SaveSettings();
    private bool _suppressProfileWarning;

    // ── Системные ──
    [ObservableProperty] private bool autoStartEnabled = false;
    partial void OnAutoStartEnabledChanged(bool value)
    {
        AutoStartService.SetEnabled(value);
        SaveSettings();
    }
    [ObservableProperty] private bool minimizeToTray = true;
    partial void OnMinimizeToTrayChanged(bool value) => SaveSettings();
    [ObservableProperty] private string updateStatus = "Не проверялось";
    [ObservableProperty] private string currentEngineVersion = "—";
    [ObservableProperty] private bool isUpdating;
    [ObservableProperty] private bool isDownloadingEngine;
    [ObservableProperty] private string engineDownloadStatus = "";
    private UpdateInfo? _pendingUpdate;

    public MainViewModel()
    {
        Logs.Add("Приложение запущено.");
        AddToRecentLogs("🚀 Приложение запущено");

        var settings = _settingsService.Load();
        ApplySettings(settings);

        LoadProfiles();

        if (settings.LastProfileFileName is not null)
        {
            var last = Profiles.FirstOrDefault(p => p.FileName == settings.LastProfileFileName);
            if (last is not null) SelectedProfile = last;
        }

        if (settings.ProfileRatings.Count > 0)
        {
            RebuildProfileScores();
            foreach (var rating in settings.ProfileRatings)
            {
                var entry = ProfileScores.FirstOrDefault(s => s.FileName == rating.FileName);
                if (entry is not null && rating.Score > 0)
                    entry.SetScore(rating.Score / 100.0);
            }
            var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
            ProfileScores.Clear();
            foreach (var s in sorted) ProfileScores.Add(s);
            Logs.Add("📊 Рейтинг профилей восстановлен.");
        }

        _settingsLoaded = true;

        if (!Directory.Exists(EngineDir) || Directory.GetFiles(EngineDir, "*.bat").Length == 0)
        {
            Logs.Add("⚠️ Папка engine/ не найдена. Скачиваем Flowseal...");
            AddToRecentLogs("⬇️ Скачивание Flowseal...");
            _ = AutoDownloadEngineAsync();
        }

        DisableNativeUpdateCheck();
        CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
        _ = CheckUpdatesOnStartupAsync();
        RefreshDiagnostics();
        RefreshServiceStatus();

        _uptimeTimer.Tick += (_, _) => UpdateRuntimeInfo();
        _uptimeTimer.Start();
        UpdateRuntimeInfo();

        _orchestrator = new OrchestratorService(
            getProfiles: () => Profiles,
            getActiveProfile: () => SelectedProfile,
            switchProfile: SwitchProfileAsync,
            getTargetsPath: () => TargetsPath,
            notifyScoreUpdate: UpdateProfileScoreAsync
        );
        _orchestrator.StatusChanged += OnOrchestratorStatus;

        _orchestratorUiTimer.Tick += (_, _) => UpdateOrchestratorNextCheck();
        _orchestratorUiTimer.Start();
    }

    // ── Настройки ──

    private void ApplySettings(AppSettings settings)
    {
        OrchestratorInterval = settings.OrchestratorInterval;
        SiteYouTube = settings.SiteYouTube;
        SiteDiscord = settings.SiteDiscord;
        SiteGoogle = settings.SiteGoogle;
        SiteTwitch = settings.SiteTwitch;
        SiteInstagram = settings.SiteInstagram;
        SiteTelegram = settings.SiteTelegram;
        AutoUpdateEnabled = settings.AutoUpdateEnabled;
        AutoStartEnabled = settings.AutoStartEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        GameFilterProtocol = settings.GameFilterProtocol;
        ShowProfileSwitchWarning = settings.ShowProfileSwitchWarning;

        // TG Proxy — сбрасываем устаревшие дефолты
        TgProxyHost = string.IsNullOrWhiteSpace(settings.TgProxy.Host) || settings.TgProxy.Host == "0.0.0.0" ? "127.0.0.1" : settings.TgProxy.Host;
        TgProxyPort = (settings.TgProxy.Port is 0 or 1080 or 3128 or 2080) ? "1443" : settings.TgProxy.Port.ToString();
        TgProxySecret = settings.TgProxy.Secret;
        TgProxyDomain = settings.TgProxy.Domain;
        TgProxyVerbose = settings.TgProxy.Verbose;
        TgProxyPreferIPv4 = settings.TgProxy.PreferIPv4;
        TgProxyDcIps = string.IsNullOrWhiteSpace(settings.TgProxy.DcIps) ? "2:149.154.167.220\n4:149.154.167.220" : settings.TgProxy.DcIps;
        TgProxyCfEnabled = settings.TgProxy.CfProxyEnabled;
        TgProxyCfPriority = settings.TgProxy.CfProxyPriority;
        TgProxyCfDomainEnabled = settings.TgProxy.CfDomainEnabled;
        TgProxyCfDomain = settings.TgProxy.CfDomain;
        TgProxyBufKb = settings.TgProxy.BufKb == 0 ? "256" : settings.TgProxy.BufKb.ToString();
        TgProxyPoolSize = settings.TgProxy.PoolSize == 0 ? "4" : settings.TgProxy.PoolSize.ToString();
        TgProxyLogMaxMb = settings.TgProxy.LogMaxMb == 0 ? "5.0" : settings.TgProxy.LogMaxMb.ToString();
    }

    public void SaveSettings()
    {
        if (!_settingsLoaded) return;

        var settings = new AppSettings
        {
            LastProfileFileName = SelectedProfile?.FileName,
            OrchestratorInterval = OrchestratorInterval,
            SiteYouTube = SiteYouTube,
            SiteDiscord = SiteDiscord,
            SiteGoogle = SiteGoogle,
            SiteTwitch = SiteTwitch,
            SiteInstagram = SiteInstagram,
            SiteTelegram = SiteTelegram,
            AutoUpdateEnabled = AutoUpdateEnabled,
            AutoStartEnabled = AutoStartEnabled,
            MinimizeToTray = MinimizeToTray,
            GameFilterProtocol = GameFilterProtocol,
            ShowProfileSwitchWarning = ShowProfileSwitchWarning,
            ProfileRatings = ProfileScores.Select(s => new ProfileRatingEntry
            {
                FileName = s.FileName,
                DisplayName = s.DisplayName,
                Score = s.Score
            }).ToList(),
            TgProxy = new FluxRoute.Core.Services.TgProxySettings
            {
                Host = TgProxyHost,
                Port = int.TryParse(TgProxyPort, out var tgPort) ? tgPort : 1443,
                Secret = TgProxySecret,
                Domain = TgProxyDomain,
                Verbose = TgProxyVerbose,
                PreferIPv4 = TgProxyPreferIPv4,
                DcIps = TgProxyDcIps,
                CfProxyEnabled = TgProxyCfEnabled,
                CfProxyPriority = TgProxyCfPriority,
                CfDomainEnabled = TgProxyCfDomainEnabled,
                CfDomain = TgProxyCfDomain,
                BufKb = int.TryParse(TgProxyBufKb, out var bufKb) ? bufKb : 256,
                PoolSize = int.TryParse(TgProxyPoolSize, out var poolSize) ? poolSize : 4,
                LogMaxMb = double.TryParse(TgProxyLogMaxMb, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var logMb) ? logMb : 5.0
            }
        };

        _settingsService.Save(settings);
    }

    // ── UI-команды ──

    [RelayCommand]
    private void SelectTab(string index) => SelectedTabIndex = int.Parse(index);

    [RelayCommand]
    private void OpenEngineFolder()
    {
        try
        {
            if (Directory.Exists(EngineDir))
                Process.Start(new ProcessStartInfo("explorer.exe", EngineDir) { UseShellExecute = true });
            else
                AddToRecentLogs("❌ Папка engine не найдена");
        }
        catch (Exception ex) { AddToRecentLogs($"❌ {ex.Message}"); }
    }

    [RelayCommand]
    private void ShowLogs() => SelectedTabIndex = 3;

    [RelayCommand]
    private void ToggleSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => OpenAboutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleLogs() => IsLogsVisible = !IsLogsVisible;

    [RelayCommand]
    private void MainAction()
    {
        if (IsRunning) Stop();
        else Start();
    }

    private void AddToRecentLogs(string message)
    {
        RecentLogs.Add(message);
        while (RecentLogs.Count > 10)
            RecentLogs.RemoveAt(0);
        LastStatusMessage = message;
    }

    // ── Cleanup ──

    public void Cleanup()
    {
        if (_orchestrator.IsRunning)
            _orchestrator.Stop();

        _uptimeTimer?.Stop();
        _orchestratorUiTimer?.Stop();

        RemoveWindowHook();
        _hideWindowsCts?.Cancel();
        _hideWindowsCts?.Dispose();
    }
}
