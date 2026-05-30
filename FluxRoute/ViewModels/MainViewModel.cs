using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace FluxRoute.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Коллекции ──
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public ObservableCollection<string> OrchestratorLogs { get; } = new();
    public ObservableCollection<ProfileScore> ProfileScores { get; } = new();
    public ObservableCollection<AiStrategyRowVm> AiStrategyRows { get; } = new();
    public int AiStrategyRowCount => AiStrategyRows.Count;
    public ObservableCollection<string> RecentLogs { get; } = new();
    public ObservableCollection<string> UpdateLogs => Updates.UpdateLogs;
    public ObservableCollection<string> ServiceLogs => Service.ServiceLogs;

    // Коллекции для менеджера доменов (UI)

    // ── Пресеты конфигурации ──
    public ObservableCollection<ConfigPreset> Presets { get; } = new();
    public ObservableCollection<string> RunningProcesses { get; } = new();

    // ── Менеджер доменов ──
    [ObservableProperty] private string selectedTabMode = "Domains";
    [ObservableProperty] private string newSiteInput = "";

    public ObservableCollection<string> CustomTargetDomains { get; } = new();
    public ObservableCollection<string> CustomExcludeDomains { get; } = new();

    [ObservableProperty] private string newPresetName = "";
    [ObservableProperty] private string newPresetTrigger = "";

    private bool _processPickerOpen;
    public bool ProcessPickerOpen
    {
        get => _processPickerOpen;
        set => SetProperty(ref _processPickerOpen, value);
    }

    [RelayCommand]
    private void OpenProcessPicker()
    {
        RunningProcesses.Clear();
        var procs = System.Diagnostics.Process.GetProcesses()
            .Select(p => { try { return p.ProcessName + ".exe"; } catch { return null; } })
            .Where(n => n is not null)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        foreach (var p in procs!)
            RunningProcesses.Add(p!);
        ProcessPickerOpen = true;
    }

    [RelayCommand]
    private void PickProcess(string processName)
    {
        NewPresetTrigger = processName;
        ProcessPickerOpen = false;
    }

    [RelayCommand]
    private void SavePresetFromCurrent()
    {
        var name = NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) name = $"Пресет {Presets.Count + 1}";

        var preset = new ConfigPreset
        {
            Name = name,
            ProfileFileName = SelectedProfile?.FileName,
            GameFilterEnabled = Service.GameFilterEnabled,
            GameFilterProtocol = Service.GameFilterProtocol,
            IpSetMode = Service.IpSetMode,
            TriggerProcess = NewPresetTrigger.Trim()
        };
        Presets.Add(preset);
        NewPresetName = "";
        NewPresetTrigger = "";
        SaveSettings();
        AddToRecentLogs($"✅ Пресет «{preset.Name}» сохранён");
    }

    [RelayCommand]
    private async Task ApplyPreset(ConfigPreset preset)
    {
        AddToRecentLogs($"⚙️ Применяем пресет «{preset.Name}»...");

        // 1. Переключить профиль
        if (!string.IsNullOrEmpty(preset.ProfileFileName))
        {
            var profile = Profiles.FirstOrDefault(p => p.FileName == preset.ProfileFileName);
            if (profile is not null && profile != SelectedProfile)
            {
                _suppressProfileWarning = true;
                SelectedProfile = profile;
                _suppressProfileWarning = false;
            }
        }

        // 2. Применить GameFilter + IPSet через сервис
        Service.ApplyPresetState(preset.GameFilterEnabled, preset.GameFilterProtocol, preset.IpSetMode);

        // 3. Перезапустить защиту
        if (IsRunning)
        {
            Stop();
            await Task.Delay(1200).ConfigureAwait(false);
            Application.Current.Dispatcher.Invoke(Start);
        }

        AddToRecentLogs($"✅ Пресет «{preset.Name}» применён");
    }

    [RelayCommand]
    private void RemovePreset(ConfigPreset preset)
    {
        Presets.Remove(preset);
        SaveSettings();
    }

    // ── Команды менеджера доменов ──
    [RelayCommand]
    private void AddCustomDomain()
    {
        var input = NewSiteInput.Trim();
        if (string.IsNullOrEmpty(input)) return;

        // Нормализация: убираем протоколы и www
        input = input.Replace("http://", "").Replace("https://", "").Replace("www.", "").TrimEnd('/');
        if (string.IsNullOrEmpty(input)) return;

        var list = SelectedTabMode == "Exclusions" ? CustomExcludeDomains : CustomTargetDomains;

        if (!list.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(input);
            NewSiteInput = "";
            SaveSettings();
            SyncCustomHostlist();
            AddToRecentLogs($"✅ Добавлен домен: {input} ({(SelectedTabMode == "Exclusions" ? "исключение" : "проверка")})");
        }
        else
        {
            NewSiteInput = "";
        }
    }

    [RelayCommand]
    private void RemoveCustomDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return;
        var list = SelectedTabMode == "Exclusions" ? CustomExcludeDomains : CustomTargetDomains;
        if (list.Contains(domain))
        {
            list.Remove(domain);
            SaveSettings();
            SyncCustomHostlist();
            AddToRecentLogs($"🗑 Удалён домен: {domain}");
        }
    }

    [RelayCommand]
    private void SetDomainsMode()
    {
        SelectedTabMode = "Domains";
        NewSiteInput = "";
    }

    [RelayCommand]
    private void SetExclusionsMode()
    {
        SelectedTabMode = "Exclusions";
        NewSiteInput = "";
    }

    [RelayCommand]
    private void ClearDomainList()
    {
        var list = SelectedTabMode == "Exclusions" ? CustomExcludeDomains : CustomTargetDomains;
        if (list.Count == 0) return;
        var modeName = SelectedTabMode == "Exclusions" ? "исключений" : "доменов";
        if (CustomDialog.Show(
            "🗑 Очистить список",
            $"Удалить все {list.Count} записей из списка {modeName}?",
            "Очистить", "Отмена", isDanger: true))
        {
            list.Clear();
            SaveSettings();
            SyncCustomHostlist();
            AddToRecentLogs($"🗑 Список {modeName} очищен");
        }
    }

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
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    SelectedProfile = oldValue;
                    _suppressProfileWarning = false;
                }, DispatcherPriority.Background);
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
        3 => "ИИ",
        4 => "ОБНОВЛЕНИЕ",
        5 => "ДИАГНОСТИКА",
        6 => "СЕРВИС",
        7 => "О ПРОГРАММЕ",
        8 => "ЛОГИ",
        _ => ""
    };
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedTabName));
        if (value == 1) OnTgProxyTabActivated();
        if (value == 2) RebuildAiStrategyRows();
        if (value == 3)
        {
            _aiOrchestrator.SyncRegistryFromEngine();
            RefreshAiDashboard();
            RebuildAiStrategyRows();
        }
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

    // ── Feature ViewModels ──
    public UpdatesViewModel Updates { get; private set; } = null!;
    public ServiceViewModel Service { get; private set; } = null!;
    public DiagnosticsViewModel Diagnostics { get; private set; } = null!;

    // ── Диагностика (wrappers → DiagnosticsViewModel) ──
    public bool IsAdmin => Diagnostics.IsAdmin;
    public string AdminText => Diagnostics.AdminText;
    public bool EngineOk => Diagnostics.EngineOk;
    public string EngineText => Diagnostics.EngineText;
    public bool WinwsOk => Diagnostics.WinwsOk;
    public string WinwsText => Diagnostics.WinwsText;
    public bool WinDivertDllOk => Diagnostics.WinDivertDllOk;
    public string WinDivertDllText => Diagnostics.WinDivertDllText;
    public bool WinDivertDriverOk => Diagnostics.WinDivertDriverOk;
    public string WinDivertDriverText => Diagnostics.WinDivertDriverText;

    // ── Оркестратор ──
    [ObservableProperty] private bool orchestratorRunning;
    [ObservableProperty] private bool orchestratorEnabled;
    partial void OnOrchestratorEnabledChanged(bool value)
    {
        SaveSettings();
        if (_settingsLoaded)
            ApplyOrchestratorEnabledState();
    }
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

    // ── Свои сайты ──
    [ObservableProperty] private string userCustomSitesText = "";
    partial void OnUserCustomSitesTextChanged(string value) => SaveSettings();

    private readonly OrchestratorService _orchestrator;
    private readonly AiOrchestratorService _aiOrchestrator;
    private readonly AiStrategyRegistry _aiRegistry;
    private readonly AiHistoryStore _aiHistoryStore;
    private readonly NetworkFingerprintProvider _aiFingerprints;
    private readonly DispatcherTimer _orchestratorUiTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1) };

    // ── Сервис (wrappers → ServiceViewModel) ──
    public bool GameFilterEnabled => Service.GameFilterEnabled;
    public string GameFilterProtocol
    {
        get => Service.GameFilterProtocol;
        set { Service.GameFilterProtocol = value; SaveSettings(); }
    }
    public List<string> GameFilterProtocols => Service.GameFilterProtocols;
    public string IpSetMode => Service.IpSetMode;
    public string ZapretServiceStatus => Service.ZapretServiceStatus;
    public bool IsServiceBusy => Service.IsServiceBusy;

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
    private string EngineDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");

    private readonly IUpdaterService _updater;
    private readonly IAppUpdaterService _appUpdater;
    private readonly ISettingsService _settingsService;
    private readonly IConnectivityChecker _connectivity;
    private bool _settingsLoaded = false;
    private bool _suppressOrchestratorStop = false;

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

    // ── Обновления (wrappers → UpdatesViewModel) ──
    public string UpdateStatus => Updates.UpdateStatus;
    public string CurrentEngineVersion => Updates.CurrentEngineVersion;
    public bool IsUpdating => Updates.IsUpdating;
    public bool IsDownloadingEngine => Updates.IsDownloadingEngine;
    public string EngineDownloadStatus => Updates.EngineDownloadStatus;

    public MainViewModel(
        ISettingsService settingsService,
        IUpdaterService updaterService,
        IAppUpdaterService appUpdaterService,
        IConnectivityChecker connectivity,
        NetworkFingerprintProvider aiFingerprints,
        NetworkChangeWatcher aiNetworkWatcher,
        AiStrategyRegistry aiRegistry,
        AiHistoryStore aiHistoryStore,
        BanditSelector aiBandit,
        StrategyEvolver aiEvolver,
        BatMaterializer aiMaterializer)
    {
        _settingsService = settingsService;
        _updater = updaterService;
        _connectivity = connectivity;
        _appUpdater = appUpdaterService;
        _aiRegistry = aiRegistry;
        _aiHistoryStore = aiHistoryStore;
        _aiFingerprints = aiFingerprints;

        // ── Инициализация feature ViewModels ──
        Diagnostics = new DiagnosticsViewModel(
            getEngineDir: () => EngineDir,
            getWinwsPath: () => WinwsPath,
            getWinDivertDllPath: () => WinDivertDllPath,
            getWinDivertSys64Path: () => WinDivertSys64Path,
            getWinDivertSysPath: () => WinDivertSysPath,
            addAppLog: msg => Logs.Add(msg));

        Service = new ServiceViewModel(
            getEngineDir: () => EngineDir,
            getSelectedProfileDisplayName: () => SelectedProfile?.DisplayName,
            addAppLog: msg => Logs.Add(msg));

        Service.GetAutoTuneTargets = () =>
        {
            var sites = new List<string>();
            if (SiteYouTube) sites.Add("YouTube");
            if (SiteDiscord) sites.Add("Discord");
            if (SiteGoogle) sites.Add("Google");
            if (SiteTwitch) sites.Add("Twitch");
            if (SiteInstagram) sites.Add("Instagram");
            if (SiteTelegram) sites.Add("Telegram");
            return sites
                .SelectMany(s => FluxRoute.Core.Services.ConnectivityChecker.BuiltinSites.TryGetValue(s, out var e) ? e : Enumerable.Empty<FluxRoute.Core.Models.TargetEntry>())
                .ToList();
        };

        Updates = new UpdatesViewModel(
            updater: _updater,
            appUpdater: _appUpdater,
            getEngineDir: () => EngineDir,
            getAutoUpdateEnabled: () => AutoUpdateEnabled,
            getCurrentEngineVersion: () => Updates.CurrentEngineVersion,
            setCurrentEngineVersion: v => Updates.CurrentEngineVersion = v,
            stopEngine: Stop,
            loadProfiles: LoadProfiles,
            refreshDiagnostics: RefreshDiagnostics,
            addAppLog: msg => Logs.Add(msg),
            addRecentLog: AddToRecentLogs);

        Diagnostics.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        Service.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        Updates.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

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
            _ = Updates.AutoDownloadEngineAsync();
        }

        DisableNativeUpdateCheck();
        Updates.CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
        _ = Updates.CheckOnStartupAsync();
        RefreshDiagnostics();
        Service.Refresh();

        _uptimeTimer.Tick += (_, _) => UpdateRuntimeInfo();
        _uptimeTimer.Start();
        UpdateRuntimeInfo();

        _orchestrator = new OrchestratorService(
            getProfiles: () => Profiles,
            getActiveProfile: () => SelectedProfile,
            switchProfile: SwitchProfileAsync,
            getTargetsPath: () => TargetsPath,
            notifyScoreUpdate: UpdateProfileScoreAsync,
            connectivity: _connectivity
        );
        _orchestrator.StatusChanged += OnOrchestratorStatus;

        if (settings.ProfileRatings.Count > 0)
        {
            var saved = settings.ProfileRatings
                .Select(r => (profile: Profiles.FirstOrDefault(p => p.FileName == r.FileName), r.Score))
                .Where(x => x.profile is not null)
                .Select(x => (x.profile!, x.Score));
            _orchestrator.RestoreRankedProfiles(saved);
        }

        _aiOrchestrator = new AiOrchestratorService(
            () => Profiles,
            () => SelectedProfile,
            SwitchProfileAsync,
            () => TargetsPath,
            UpdateProfileScoreAsync,
            () => EngineDir,
            BuildAiSettingsSnapshot,
            RefreshProfilesInternalAsync,
            IsTrackedProcessRunning,
            EnsureProtectionRunningAsync,
            connectivity,
            aiFingerprints,
            aiNetworkWatcher,
            aiRegistry,
            aiHistoryStore,
            aiBandit,
            aiEvolver,
            aiMaterializer);
        _aiOrchestrator.StatusChanged += OnOrchestratorStatus;

        _orchestratorUiTimer.Tick += (_, _) => UpdateOrchestratorNextCheck();
        _orchestratorUiTimer.Start();
        _aiOrchestrator.SyncRegistryFromEngine();
        RebuildAiStrategyRows();
        InitializeTgProxyOnStartup();
    }

    // ── Настройки ──

    private void ApplySettings(AppSettings settings)
    {
        OrchestratorInterval = settings.OrchestratorInterval;
        OrchestratorEnabled = settings.OrchestratorEnabled;
        SiteYouTube = settings.SiteYouTube;
        SiteDiscord = settings.SiteDiscord;
        SiteGoogle = settings.SiteGoogle;
        SiteTwitch = settings.SiteTwitch;
        SiteInstagram = settings.SiteInstagram;
        SiteTelegram = settings.SiteTelegram;
        UserCustomSitesText = string.Join("\n", settings.UserSites ?? new());

        // Миграция старых данных в новые списки менеджера доменов
        CustomTargetDomains.Clear();
        CustomExcludeDomains.Clear();

        if (settings.CustomTargetDomains?.Count > 0)
        {
            foreach (var s in settings.CustomTargetDomains) CustomTargetDomains.Add(s);
        }
        else if (settings.UserSites?.Count > 0)
        {
            foreach (var s in settings.UserSites.Where(x => !x.StartsWith("!"))) CustomTargetDomains.Add(s);
        }

        if (settings.CustomExcludeDomains?.Count > 0)
        {
            foreach (var s in settings.CustomExcludeDomains) CustomExcludeDomains.Add(s);
        }
        else if (settings.UserSites?.Count > 0)
        {
            foreach (var s in settings.UserSites.Where(x => x.StartsWith("!"))) CustomExcludeDomains.Add(s.TrimStart('!'));
        }

        AutoUpdateEnabled = settings.AutoUpdateEnabled;
        AutoStartEnabled = settings.AutoStartEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        GameFilterProtocol = settings.GameFilterProtocol;
        ShowProfileSwitchWarning = settings.ShowProfileSwitchWarning;

        settings.Ai ??= new AiSettings();
        AiEnabled = settings.Ai.Enabled;
        AiExplorationPermil = settings.Ai.ExplorationRatePermil;
        AiAutoDeleteBelowScore = settings.Ai.AutoDeleteBelowScore;

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
        TgProxyAutoStartOnAppLaunch = settings.TgProxy.AutoStartOnAppLaunch;
        TgProxyBufKb = settings.TgProxy.BufKb == 0 ? "256" : settings.TgProxy.BufKb.ToString();
        TgProxyPoolSize = settings.TgProxy.PoolSize == 0 ? "4" : settings.TgProxy.PoolSize.ToString();
        TgProxyLogMaxMb = settings.TgProxy.LogMaxMb == 0 ? "5.0" : settings.TgProxy.LogMaxMb.ToString();

        Presets.Clear();
        foreach (var p in settings.Presets ?? new())
            Presets.Add(p);
    }

    public void SaveSettings()
    {
        if (!_settingsLoaded) return;

        var settings = new AppSettings
        {
            LastProfileFileName = SelectedProfile?.FileName,
            OrchestratorInterval = OrchestratorInterval,
            OrchestratorEnabled = OrchestratorEnabled,
            SiteYouTube = SiteYouTube,
            SiteDiscord = SiteDiscord,
            SiteGoogle = SiteGoogle,
            SiteTwitch = SiteTwitch,
            SiteInstagram = SiteInstagram,
            SiteTelegram = SiteTelegram,
            UserSites = UserCustomSitesText
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .ToList(),
            CustomTargetDomains = CustomTargetDomains.ToList(),
            CustomExcludeDomains = CustomExcludeDomains.ToList(),
            AutoUpdateEnabled = AutoUpdateEnabled,
            AutoStartEnabled = AutoStartEnabled,
            MinimizeToTray = MinimizeToTray,
            GameFilterProtocol = GameFilterProtocol,
            ShowProfileSwitchWarning = ShowProfileSwitchWarning,
            Ai = BuildAiSettingsSnapshot(),
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
                AutoStartOnAppLaunch = TgProxyAutoStartOnAppLaunch,
                BufKb = int.TryParse(TgProxyBufKb, out var bufKb) ? bufKb : 256,
                PoolSize = int.TryParse(TgProxyPoolSize, out var poolSize) ? poolSize : 4,
                LogMaxMb = double.TryParse(TgProxyLogMaxMb, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var logMb) ? logMb : 5.0
            },
            Presets = Presets.ToList()
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
    private void ShowLogs() => SelectedTabIndex = 8;

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

    // ── Синхронизация пользовательских доменов с движком (winws.exe) ──
    // ── Синхронизация пользовательских доменов с движком (winws.exe) ──
    private void SyncCustomHostlist()
    {
        try
        {
            var listsDir = Path.Combine(EngineDir, "lists");
            Directory.CreateDirectory(listsDir);
            var userHostlistPath = Path.Combine(listsDir, "list-general-user.txt");

            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Берем домены из нового Менеджера доменов (вкладка "Домены")
            foreach (var d in CustomTargetDomains)
            {
                if (!string.IsNullOrWhiteSpace(d))
                    domains.Add(d.Trim());
            }

            // 2. Подхватываем из старого TextBox (для обратной совместимости)
            if (!string.IsNullOrWhiteSpace(UserCustomSitesText))
            {
                var legacy = UserCustomSitesText
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => !s.StartsWith("!"));
                foreach (var d in legacy)
                    domains.Add(d.Trim());
            }

            // Записываем в файл
            if (domains.Count > 0)
            {
                // Используем явное удаление + запись, чтобы избежать кэширования
                if (File.Exists(userHostlistPath))
                {
                    try { File.SetAttributes(userHostlistPath, FileAttributes.Normal); } catch { }
                }
                File.WriteAllLines(userHostlistPath, domains.OrderBy(x => x), new UTF8Encoding(false));
                Logs.Add($"[Sync] Записано {domains.Count} доменов в list-general-user.txt");
            }
            else
            {
                // Пустой список — удаляем файл или пишем комментарий
                if (File.Exists(userHostlistPath))
                    File.Delete(userHostlistPath);
                else
                    File.WriteAllText(userHostlistPath, "# custom domains empty\n", new UTF8Encoding(false));
                Logs.Add("[Sync] list-general-user.txt очищен");
            }
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка записи list-general-user.txt: {ex.Message}");
            Logs.Add($"   Stack: {ex.StackTrace?.Split('\n')[0]}");
        }
    }

    // ── Cleanup ──

    public void Cleanup()
    {
        if (_orchestrator.IsRunning)
            _orchestrator.Stop();
        if (_aiOrchestrator.IsRunning)
            _aiOrchestrator.Stop();

        _uptimeTimer?.Stop();
        _orchestratorUiTimer?.Stop();

        RemoveWindowHook();
        _hideWindowsCts?.Cancel();
        _hideWindowsCts?.Dispose();
    }
}