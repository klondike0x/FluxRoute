using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel : ObservableObject
{
    #region Win32 API
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);
    private const int SW_HIDE = 0;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize; public uint cntUsage; public uint th32ProcessID;
        public IntPtr th32DefaultHeapID; public uint th32ModuleID; public uint cntThreads;
        public uint th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    #endregion

    // ── Коллекции ──
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public ObservableCollection<string> OrchestratorLogs { get; } = new();
    public ObservableCollection<ProfileScore> ProfileScores { get; } = new();
    public ObservableCollection<string> RecentLogs { get; } = new();
    public ObservableCollection<string> UpdateLogs { get; } = new();

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
        1 => "ОРКЕСТРАТОР",
        2 => "ОБНОВЛЕНИЕ",
        3 => "ДИАГНОСТИКА",
        4 => "СЕРВИС",
        5 => "О ПРОГРАММЕ",
        _ => ""
    };
    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(SelectedTabName));

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

    private static readonly Dictionary<string, string> _protocolToFile = new()
    {
        ["TCP и UDP"] = "all",
        ["TCP"] = "tcp",
        ["UDP"] = "udp"
    };

    private static readonly Dictionary<string, string> _fileToProtocol = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = "TCP и UDP",
        ["tcp"] = "TCP",
        ["udp"] = "UDP"
    };

    private string ProtocolToFileValue(string protocol) =>
        _protocolToFile.TryGetValue(protocol, out var v) ? v : "udp";

    private string FileValueToProtocol(string fileValue) =>
        _fileToProtocol.TryGetValue(fileValue, out var v) ? v : "UDP";

    [ObservableProperty] private string ipSetMode = "—";
    [ObservableProperty] private string zapretServiceStatus = "—";
    [ObservableProperty] private bool isServiceBusy;
    public ObservableCollection<string> ServiceLogs { get; } = new();

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

        // Загружаем настройки ДО загрузки профилей
        var settings = _settingsService.Load();
        ApplySettings(settings);

        LoadProfiles();

        // Восстанавливаем последний профиль
        if (settings.LastProfileFileName is not null)
        {
            var last = Profiles.FirstOrDefault(p => p.FileName == settings.LastProfileFileName);
            if (last is not null) SelectedProfile = last;
        }

        // Восстанавливаем рейтинг профилей если есть сохранённый
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

        _settingsLoaded = true; // Теперь можно сохранять

        // Первый запуск: если engine/ нет — автоскачать Flowseal
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

    // ── Настройки: сохранение / загрузка ──

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
            }).ToList()
        };

        _settingsService.Save(settings);
    }

    // ── Компактный интерфейс: команды ──

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
    private void ToggleSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenAbout()
    {
        OpenAboutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleLogs()
    {
        IsLogsVisible = !IsLogsVisible;
    }

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

    // ── Оркестратор: методы ──

    private const int MaxLogEntries = 50;

    private void AddOrchestratorLog(string message)
    {
        OrchestratorLogs.Add(message);
        while (OrchestratorLogs.Count > MaxLogEntries)
            OrchestratorLogs.RemoveAt(0);
    }

    private void OnOrchestratorStatus(object? sender, OrchestratorEventArgs e)
    {
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            AddOrchestratorLog(e.Message);
            OrchestratorStatus = e.Message;

            if (e.Message.Contains("Сканирование завершено"))
            {
                var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
                ProfileScores.Clear();
                foreach (var s in sorted) ProfileScores.Add(s);
                SaveSettings(); // Сохраняем рейтинг после сканирования
            }

            if (e.IsSwitched && e.NewProfile is not null)
            {
                var profile = Profiles.FirstOrDefault(p => p.DisplayName == e.NewProfile);
                if (profile is not null)
                {
                    SelectedProfile = profile;
                    CurrentStrategy = profile.DisplayName;
                    Logs.Add($"[Оркестратор] Переключено на «{profile.DisplayName}»");
                    ProfileSwitchNotification?.Invoke(this, profile.DisplayName);
                }
            }
        });
    }

    private void UpdateOrchestratorNextCheck()
    {
        if (_orchestrator.NextCheckAt is { } next)
        {
            var remaining = next - DateTimeOffset.Now;
            OrchestratorNextCheck = remaining > TimeSpan.Zero ? $"через {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}" : "сейчас...";
        }
        else OrchestratorNextCheck = "—";
    }

    private async Task SwitchProfileAsync(ProfileItem profile)
    {
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Stop();
            if (profile is not null)
            {
                _suppressProfileWarning = true;
                SelectedProfile = profile;
                _suppressProfileWarning = false;
                Start();
            }
        });
    }

    private Task UpdateProfileScoreAsync(string fileName, int score)
    {
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return Task.CompletedTask;

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var entry = ProfileScores.FirstOrDefault(s => s.FileName == fileName);
            if (entry is null) return;
            if (score == -1) entry.SetPending();
            else entry.SetScore(score / 100.0);
        }).Task;
    }

    private void RebuildProfileScores()
    {
        ProfileScores.Clear();
        foreach (var p in Profiles)
            ProfileScores.Add(new ProfileScore { DisplayName = p.DisplayName, FileName = p.FileName });
    }

    private void UpdateOrchestratorEnabledSites()
    {
        var sites = new HashSet<string>();
        if (SiteYouTube) sites.Add("YouTube");
        if (SiteDiscord) sites.Add("Discord");
        if (SiteGoogle) sites.Add("Google");
        if (SiteTwitch) sites.Add("Twitch");
        if (SiteInstagram) sites.Add("Instagram");
        if (SiteTelegram) sites.Add("Telegram");
        _orchestrator.EnabledSites = sites;
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!AutoUpdateEnabled) return;

        var (update, _) = await _updater.CheckForUpdateAsync(EngineDir);
        if (update is null) return;

        _pendingUpdate = update;
        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateStatus = $"Доступна новая версия: {update.Version}";

                if (CustomDialog.Show(
                    "⬆️ Обновление доступно",
                    $"Доступно обновление Flowseal zapret!\n\nВерсия: {update.Version}\n\nОбновить сейчас?",
                    "Обновить", "Позже"))
                    _ = InstallUpdateAsync();
            });
        }
    }

    [RelayCommand]
    private async Task ScanProfiles()
    {
        RebuildProfileScores();
        IsScanning = true;
        ScanProgressText = "Сканирование...";
        UpdateOrchestratorEnabledSites();

        await _orchestrator.ScanAllProfilesAsync();

        var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
        ProfileScores.Clear();
        foreach (var s in sorted) ProfileScores.Add(s);

        IsScanning = false;
        ScanProgressText = "Сканирование завершено";
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleOrchestrator()
    {
        if (_orchestrator.IsRunning)
        {
            _orchestrator.Stop();
            OrchestratorRunning = false;
        }
        else
        {
            if (int.TryParse(OrchestratorInterval, out int mins) && mins >= 1)
                _orchestrator.CheckInterval = TimeSpan.FromMinutes(mins);

            UpdateOrchestratorEnabledSites();

            // Перестраиваем только если рейтинг ещё не был получен
            if (ProfileScores.Count == 0 || ProfileScores.All(s => s.Score == 0))
                RebuildProfileScores();

            if (_runningProcess is null || _runningProcess.HasExited)
            {
                if (SelectedProfile is not null) { Logs.Add("[Оркестратор] Автозапуск профиля..."); Start(); }
                else { Logs.Add("[Оркестратор] Профиль не выбран."); return; }
            }

            _orchestrator.Start();
            OrchestratorRunning = true;
        }
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] Запуск ручной проверки...");
        await _orchestrator.CheckNowAsync();
    }

    [RelayCommand]
    private void ClearOrchestratorLogs()
    {
        OrchestratorLogs.Clear();
    }

    // ── Основные команды ──

    [RelayCommand]
    private void RefreshProfiles() { Logs.Add("Обновляем список профилей..."); LoadProfiles(); RefreshDiagnostics(); }

    [RelayCommand]
    private void ToggleStartStop()
    {
        if (IsRunning) Stop();
        else Start();
    }

    [RelayCommand]
    private void Start()
    {
        if (IsRunning) { Logs.Add("Процесс уже запущен."); return; }
        if (SelectedProfile is null) { Logs.Add("Профиль не выбран."); AddToRecentLogs("❌ Профиль не выбран"); return; }
        if (!File.Exists(SelectedProfile.FullPath)) { Logs.Add($"BAT не найден: {SelectedProfile.FullPath}"); AddToRecentLogs("❌ BAT не найден"); return; }

        InstallWindowHook();

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{SelectedProfile.FullPath}\"",
            WorkingDirectory = EngineDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            var cmdProcess = Process.Start(psi);
            if (cmdProcess is null) { Logs.Add("Не удалось запустить процесс."); AddToRecentLogs("❌ Ошибка запуска"); return; }
            StatusText = "Запущено";
            CurrentStrategy = SelectedProfile.DisplayName;
            RunningScriptName = SelectedProfile.FileName;
            _runStartedAt = DateTimeOffset.Now;
            Logs.Add($"Запуск: {RunningScriptName}");
            AddToRecentLogs($"▶ Запуск: {RunningScriptName}");
            _ = TrackWinwsAsync(cmdProcess);
            RefreshDiagnostics();
            UpdateRuntimeInfo();
        }
        catch (Exception ex) { Logs.Add($"Ошибка запуска: {ex.Message}"); }
    }

    private async Task TrackWinwsAsync(Process cmdProcess)
    {
        _hideWindowsCts?.Cancel();
        _hideWindowsCts?.Dispose();
        _hideWindowsCts = new CancellationTokenSource();
        var ct = _hideWindowsCts.Token;

        Process? winws = null;
        for (int i = 0; i < 100 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
            var rootPid = !cmdProcess.HasExited ? (uint)cmdProcess.Id : 0;
            var pids = rootPid > 0 ? GetProcessTreePids(rootPid) : new HashSet<uint>();

            try { foreach (var c in Process.GetProcessesByName("winws")) pids.Add((uint)c.Id); } catch { }
            _trackedPids = pids;

            if (winws is null)
                try { winws = Process.GetProcessesByName("winws").FirstOrDefault(); } catch { }

            if (winws is not null) break;
        }

        if (winws is null)
        {
            RemoveWindowHook();
            Logs.Add("winws.exe не найден после запуска BAT.");
            AddToRecentLogs("❌ winws.exe не найден");
            return;
        }

        _trackedPids = new HashSet<uint>(_trackedPids) { (uint)winws.Id };
        HideWindowsForPids(_trackedPids);

        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _runningProcess = winws;
                PidText = winws.Id.ToString();
                IsRunning = true;
                Logs.Add($"winws.exe запущен, PID: {winws.Id}");
                AddToRecentLogs($"✅ Запущен (PID: {winws.Id})");
            });
        }

        try { await winws.WaitForExitAsync(ct); } catch (OperationCanceledException) { }

        RemoveWindowHook();

        if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    StatusText = "Не запущено";
                    CurrentStrategy = "—";
                    RunningScriptName = "—";
                    PidText = "—"; UptimeText = "—"; _runStartedAt = null; _runningProcess = null;
                    IsRunning = false;
                    Logs.Add("winws.exe завершился.");
                    AddToRecentLogs("⏹ Завершён");
                }
            });
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _hideWindowsCts?.Cancel();
        RemoveWindowHook();

        var pidsToKill = new HashSet<uint>(_trackedPids);
        if (_runningProcess is not null && !_runningProcess.HasExited)
            pidsToKill.UnionWith(GetProcessTreePids((uint)_runningProcess.Id));

        try { foreach (var p in Process.GetProcessesByName("winws")) pidsToKill.Add((uint)p.Id); } catch { }

        if (pidsToKill.Count == 0) { Logs.Add("Нет запущенного процесса."); AddToRecentLogs("⏹ Нет активного процесса"); StatusText = "Не запущено"; return; }

        int killed = 0;
        foreach (var pid in pidsToKill)
        {
            try { var p = Process.GetProcessById((int)pid); p.Kill(entireProcessTree: true); p.WaitForExit(3000); killed++; } catch { }
        }

        for (int i = 0; i < 20; i++)
        {
            try { if (Process.GetProcessesByName("winws").Length == 0) break; } catch { break; }
            Thread.Sleep(100);
        }

        Logs.Add($"Остановлено процессов: {killed} ({RunningScriptName})");
        AddToRecentLogs($"⏹ Остановлено: {RunningScriptName}");
        _trackedPids = []; _runningProcess?.Dispose(); _runningProcess = null;
        StatusText = "Остановлено";
        CurrentStrategy = "—";
        RunningScriptName = "—"; PidText = "—"; UptimeText = "—";
        IsRunning = false;
        _runStartedAt = null; RefreshDiagnostics(); UpdateRuntimeInfo();
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        UpdateStatus = "🔍 Проверяем обновления...";
        var (update, error) = await _updater.CheckForUpdateAsync(EngineDir);

        if (update is null)
        {
            if (error is not null)
            {
                UpdateStatus = $"❌ {error}";
                Logs.Add($"Ошибка проверки: {error}");
                UpdateLogs.Add($"❌ {error}");
            }
            else
            {
                UpdateStatus = $"✅ Актуальная версия ({CurrentEngineVersion})";
                Logs.Add("Обновлений не найдено.");
                UpdateLogs.Add("✅ Обновлений не найдено");
            }
            return;
        }

        _pendingUpdate = update;
        UpdateStatus = $"⬆️ Доступна версия {update.Version}";
        Logs.Add($"Доступно обновление: {update.Version}");
        UpdateLogs.Add($"⬆️ Доступно: {update.Version}");
    }

    [RelayCommand]
    private async Task InstallUpdates()
    {
        if (_pendingUpdate is null)
        {
            await CheckUpdates();
            if (_pendingUpdate is null)
            {
                // Обычная проверка не нашла обновлений — предлагаем принудительную переустановку
                UpdateStatus = "🔄 Принудительная проверка...";
                var (latest, forceError) = await _updater.GetLatestReleaseAsync();
                if (latest is null)
                {
                    var errMsg = forceError ?? "Неизвестная ошибка";
                    UpdateStatus = $"❌ {errMsg}";
                    UpdateLogs.Add($"❌ {errMsg}");
                    return;
                }

                if (!CustomDialog.Show(
                    "🔄 Принудительное обновление",
                    $"Локальная версия совпадает с последней ({latest.Version}).\n\nПринудительно переустановить Flowseal?\nЭто скачает и заменит все файлы engine/.",
                    "Переустановить", "Отмена", isDanger: true))
                {
                    UpdateStatus = $"✅ Актуальная версия ({CurrentEngineVersion})";
                    return;
                }

                _pendingUpdate = latest;
                UpdateLogs.Add($"🔄 Принудительная переустановка {latest.Version}...");
            }
        }
        await InstallUpdateAsync();
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        IsUpdating = true;
        Stop();

        var success = await _updater.InstallUpdateAsync(EngineDir, _pendingUpdate,
            msg =>
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateStatus = msg;
                        Logs.Add(msg);
                        UpdateLogs.Add(msg);
                    });
                }
            });

        if (success)
        {
            CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
            _pendingUpdate = null;
            LoadProfiles();
        }
        else
        {
            UpdateLogs.Add("⚠️ Нажмите «Обновить» для повторной попытки");
        }

        IsUpdating = false;
    }

    private async Task AutoDownloadEngineAsync()
    {
        IsDownloadingEngine = true;
        EngineDownloadStatus = "🔍 Поиск последней версии Flowseal...";

        try
        {
            var (update, error) = await _updater.GetLatestReleaseAsync();
            if (update is null)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EngineDownloadStatus = $"❌ {error ?? "Не удалось получить информацию о релизе"}";
                        Logs.Add($"❌ Flowseal: {error ?? "неизвестная ошибка"}");
                        IsDownloadingEngine = false;
                    });
                }
                return;
            }

            // Подтверждение перед первым скачиванием — прозрачность источника
            var confirmed = false;
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                confirmed = Application.Current.Dispatcher.Invoke(() =>
                    CustomDialog.Show(
                        "⬇️ Скачивание Flowseal",
                        $"Для работы FluxRoute необходим движок Flowseal (v{update.Version}).\n\n" +
                        $"Источник: официальный GitHub-репозиторий\n" +
                        $"github.com/Flowseal/zapret-discord-youtube\n\n" +
                        $"Ссылка на скачивание:\n{update.DownloadUrl}\n\n" +
                        $"Это open-source проект — исходный код доступен публично.\n" +
                        $"После скачивания SHA-256 хеш будет отображён в логах.",
                        "Скачать", "Отмена"));
            }

            if (!confirmed)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EngineDownloadStatus = "⏹ Скачивание отменено пользователем";
                        Logs.Add("⏹ Пользователь отменил скачивание Flowseal");
                        IsDownloadingEngine = false;
                    });
                }
                return;
            }

            var success = await _updater.InstallUpdateAsync(EngineDir, update,
                msg =>
                {
                    if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            EngineDownloadStatus = msg;
                            Logs.Add(msg);
                        });
                    }
                });

            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
                        EngineDownloadStatus = $"✅ Flowseal {update.Version} установлен!";
                        Logs.Add($"✅ Flowseal {update.Version} установлен автоматически");
                        AddToRecentLogs($"✅ Flowseal {update.Version} установлен");
                        LoadProfiles();
                        RefreshDiagnostics();
                    }
                    else
                    {
                        Logs.Add("❌ Установка Flowseal не завершена");
                        AddToRecentLogs("❌ Ошибка установки Flowseal");
                    }
                    IsDownloadingEngine = false;
                });
            }
        }
        catch (Exception ex)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    EngineDownloadStatus = $"❌ Ошибка: {ex.Message}";
                    Logs.Add($"❌ Автоскачивание Flowseal: {ex.Message}");
                    IsDownloadingEngine = false;
                });
            }
        }
    }

    [RelayCommand] private void ApplyProfile() { if (SelectedProfile is null) { Logs.Add("Профиль не выбран."); return; } Logs.Add($"Выбран профиль: {SelectedProfile.FileName}"); }
    [RelayCommand] private void CopyDiagnostics() { try { Clipboard.SetText(BuildDiagnosticsText()); Logs.Add("Диагностика скопирована."); } catch (Exception ex) { Logs.Add($"Ошибка: {ex.Message}"); } }

    [RelayCommand]
    private void ExportLogs()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Текстовый файл (*.txt)|*.txt",
                FileName = $"FluxRoute_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt",
                Title = "Экспорт логов"
            };

            if (dialog.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine($"FluxRoute v{AppVersion} — Лог от {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine();

            sb.AppendLine("── Системный лог ──");
            foreach (var line in Logs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Лог оркестратора ──");
            foreach (var line in OrchestratorLogs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Лог обновлений ──");
            foreach (var line in UpdateLogs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Диагностика ──");
            sb.Append(BuildDiagnosticsText());

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            Logs.Add($"📄 Логи экспортированы: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка экспорта логов: {ex.Message}");
        }
    }

    // ── Сервис: команды ──

    private void AddServiceLog(string message)
    {
        ServiceLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (ServiceLogs.Count > 50)
            ServiceLogs.RemoveAt(0);
    }

    private void RefreshServiceStatus()
    {
        // Game Filter
        if (File.Exists(GameFilterFlagPath))
        {
            GameFilterEnabled = true;
            try
            {
                var content = File.ReadAllText(GameFilterFlagPath).Trim();
                GameFilterProtocol = FileValueToProtocol(content);
            }
            catch { }
        }
        else
        {
            GameFilterEnabled = false;
        }

        // IPSet mode
        if (!File.Exists(IpSetFilePath))
        {
            IpSetMode = "—";
        }
        else
        {
            try
            {
                var lines = File.ReadAllLines(IpSetFilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                if (lines.Length == 0)
                    IpSetMode = "any";
                else if (lines.Length == 1 && lines[0].Trim() == "203.0.113.113/32")
                    IpSetMode = "none";
                else
                    IpSetMode = "loaded";
            }
            catch { IpSetMode = "—"; }
        }

        // Zapret service
        try
        {
            using var sc = new Process
            {
                StartInfo = new ProcessStartInfo("sc", "query zapret")
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            };
            sc.Start();
            var output = sc.StandardOutput.ReadToEnd();
            sc.WaitForExit(3000);

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "✅ Запущена";
            else if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "⏹ Остановлена";
            else if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "⚠️ Останавливается...";
            else
                ZapretServiceStatus = "❌ Не установлена";
        }
        catch
        {
            ZapretServiceStatus = "❌ Не установлена";
        }
    }

    [RelayCommand]
    private void ToggleGameFilter()
    {
        try
        {
            var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
            Directory.CreateDirectory(utilsDir);

            if (File.Exists(GameFilterFlagPath))
            {
                File.Delete(GameFilterFlagPath);
                GameFilterEnabled = false;
                AddServiceLog("🎮 Game Filter выключен");
                Logs.Add("Game Filter выключен");
            }
            else
            {
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(GameFilterProtocol));
                GameFilterEnabled = true;
                AddServiceLog($"🎮 Game Filter включён ({GameFilterProtocol})");
                Logs.Add($"Game Filter включён ({GameFilterProtocol})");
            }

            AddServiceLog("⚠️ Перезапустите zapret для применения изменений");
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CycleIpSetMode()
    {
        try
        {
            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);

            if (IpSetMode == "loaded")
            {
                // loaded → none: бэкап + заглушка
                if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                if (File.Exists(IpSetFilePath)) File.Move(IpSetFilePath, IpSetBackupPath);
                File.WriteAllText(IpSetFilePath, "203.0.113.113/32\r\n");
                IpSetMode = "none";
                AddServiceLog("🔒 IPSet → none (фильтрация отключена)");
            }
            else if (IpSetMode == "none")
            {
                // none → any: пустой файл
                File.WriteAllText(IpSetFilePath, "");
                IpSetMode = "any";
                AddServiceLog("🌐 IPSet → any (все адреса)");
            }
            else
            {
                // any / — → loaded: восстановить бэкап
                if (File.Exists(IpSetBackupPath))
                {
                    if (File.Exists(IpSetFilePath)) File.Delete(IpSetFilePath);
                    File.Move(IpSetBackupPath, IpSetFilePath);
                    IpSetMode = "loaded";
                    AddServiceLog("📋 IPSet → loaded (список восстановлен)");
                }
                else
                {
                    AddServiceLog("⚠️ Нет бэкапа IPSet. Обновите список через кнопку ниже");
                    return;
                }
            }

            AddServiceLog("⚠️ Перезапустите zapret для применения изменений");
            RefreshServiceStatus();
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateIpSetList()
    {
        IsServiceBusy = true;
        AddServiceLog("⬇️ Скачиваем ipset-all.txt...");

        try
        {
            var url = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var content = await http.GetStringAsync(url);

            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);
            await File.WriteAllTextAsync(IpSetFilePath, content);

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog($"✅ IPSet обновлён ({content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} записей)");
                RefreshServiceStatus();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog($"❌ Ошибка скачивания IPSet: {ex.Message}");
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private async Task UpdateHostsFile()
    {
        IsServiceBusy = true;
        AddServiceLog("⬇️ Проверяем hosts файл...");

        try
        {
            var hostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var newContent = await http.GetStringAsync(hostsUrl);
            var newLines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

            if (!File.Exists(hostsPath))
            {
                Application.Current.Dispatcher.Invoke(() => AddServiceLog("❌ Файл hosts не найден"));
                return;
            }

            var currentHosts = await File.ReadAllTextAsync(hostsPath);
            var firstLine = newLines.FirstOrDefault()?.Trim() ?? "";
            var lastLine = newLines.LastOrDefault()?.Trim() ?? "";

            if (currentHosts.Contains(firstLine) && currentHosts.Contains(lastLine))
            {
                Application.Current.Dispatcher.Invoke(() => AddServiceLog("✅ Hosts файл актуален"));
                return;
            }

            // Сохраняем во временный файл и открываем
            var tempFile = Path.Combine(Path.GetTempPath(), "zapret_hosts.txt");
            await File.WriteAllTextAsync(tempFile, newContent);

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog("⚠️ Hosts нужно обновить — открыты оба файла");
                AddServiceLog($"  Источник: {tempFile}");
                AddServiceLog($"  Цель: {hostsPath}");
                Process.Start(new ProcessStartInfo("notepad.exe", tempFile) { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{hostsPath}\"") { UseShellExecute = true });
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() => AddServiceLog($"❌ Ошибка: {ex.Message}"));
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private void InstallZapretService()
    {
        if (SelectedProfile is null)
        {
            AddServiceLog("❌ Сначала выберите профиль");
            return;
        }

        AddServiceLog($"🔧 Установка службы zapret с профилем «{SelectedProfile.DisplayName}»...");
        AddServiceLog("⚠️ Запускаем service.bat — следуйте инструкциям в консоли");

        try
        {
            var serviceBat = Path.Combine(EngineDir, "service.bat");
            if (!File.Exists(serviceBat))
            {
                AddServiceLog("❌ service.bat не найден в engine/");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{serviceBat}\" admin",
                WorkingDirectory = EngineDir,
                UseShellExecute = true,
                Verb = "runas"
            });

            AddServiceLog("✅ service.bat запущен с правами администратора");
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForceStopZapretService()
    {
        if (!CustomDialog.Show(
            "⚠️ Подтверждение остановки",
            "Вы действительно хотите принудительно остановить службу zapret?\n\nВсе активные соединения через zapret будут прерваны.",
            "Остановить", "Отмена", isDanger: true)) return;

        AddServiceLog("⏹ Принудительная остановка службы zapret...");

        try
        {
            var commands = "net stop zapret >nul 2>&1 & taskkill /IM winws.exe /F >nul 2>&1 & net stop WinDivert >nul 2>&1 & echo Done & timeout /t 2";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {commands}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false
            });

            AddServiceLog("✅ Команды остановки отправлены");
            // Обновим статус через пару секунд
            _ = Task.Delay(3000).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(RefreshServiceStatus));
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshServiceInfo()
    {
        RefreshServiceStatus();
        AddServiceLog("🔄 Статус обновлён");
    }

    // ── Вспомогательные ──

    private void DisableNativeUpdateCheck()
    {
        try
        {
            var flagFile = Path.Combine(EngineDir, "utils", "check_updates.enabled");
            if (File.Exists(flagFile)) { File.Delete(flagFile); Logs.Add("Встроенная проверка обновлений zapret отключена."); }
        }
        catch (Exception ex) { Logs.Add($"Не удалось отключить проверку обновлений: {ex.Message}"); }
    }

    private void RefreshDiagnostics()
    {
        IsAdmin = CheckIsAdmin();
        EngineOk = Directory.Exists(EngineDir);
        WinwsOk = File.Exists(WinwsPath);
        WinDivertDllOk = File.Exists(WinDivertDllPath);
        WinDivertDriverOk = File.Exists(WinDivertSys64Path) || File.Exists(WinDivertSysPath);
    }

    private void UpdateRuntimeInfo()
    {
        if (_runningProcess is { HasExited: false } && _runStartedAt is not null)
        {
            var ts = DateTimeOffset.Now - _runStartedAt.Value;
            UptimeText = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            PidText = _runningProcess.Id.ToString();
            IsRunning = true;
            return;
        }
        UptimeText = "—"; PidText = "—";
        if (_runningProcess is { HasExited: true })
        {
            _runningProcess.Dispose();
            _runningProcess = null;
            StatusText = "Не запущено";
            CurrentStrategy = "—";
            RunningScriptName = "—";
            IsRunning = false;
        }
    }

    private static bool CheckIsAdmin() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    private static string GetAppVersion() { var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(); return asm.GetName().Version?.ToString(3) ?? "—"; }

    private void LoadProfiles()
    {
        // Запоминаем текущий профиль
        var currentFileName = SelectedProfile?.FileName;

        Profiles.Clear();
        if (!Directory.Exists(EngineDir)) { Logs.Add($"Папка engine не найдена: {EngineDir}"); SelectedProfile = null; return; }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat", "service,.bat" };
        var bats = Directory.EnumerateFiles(EngineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !excluded.Contains(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var bat in bats)
            Profiles.Add(new ProfileItem { FileName = Path.GetFileName(bat), DisplayName = Path.GetFileNameWithoutExtension(bat), FullPath = bat });

        // Восстанавливаем текущий профиль, если он есть в новом списке
        if (currentFileName is not null)
            SelectedProfile = Profiles.FirstOrDefault(p => p.FileName == currentFileName)
                              ?? Profiles.FirstOrDefault();
        else
            SelectedProfile ??= Profiles.FirstOrDefault();

        Logs.Add($"Профили загружены: {Profiles.Count} (.bat)");
    }

    private string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("FluxRoute Desktop"); sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Admin: {(IsAdmin ? "Yes" : "No")}"); sb.AppendLine($"Engine: {EngineText} ({EngineDir})");
        sb.AppendLine($"winws.exe: {WinwsText}"); sb.AppendLine($"WinDivert.dll: {WinDivertDllText}");
        sb.AppendLine($"WinDivert.sys: {WinDivertDriverText}"); sb.AppendLine($"Status: {StatusText}");
        sb.AppendLine($"Running BAT: {RunningScriptName}"); sb.AppendLine($"PID: {PidText}");
        sb.AppendLine($"Uptime: {UptimeText}"); sb.AppendLine($"Orchestrator: {(OrchestratorRunning ? "Running" : "Stopped")}");
        return sb.ToString();
    }

    private static HashSet<uint> GetProcessTreePids(uint rootPid)
    {
        var pids = new HashSet<uint> { rootPid };
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero) return pids;
        try
        {
            var entries = new List<(uint pid, uint parentPid)>();
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
                do { entries.Add((entry.th32ProcessID, entry.th32ParentProcessID)); } while (Process32Next(snapshot, ref entry));
            var queue = new Queue<uint>(); queue.Enqueue(rootPid);
            while (queue.Count > 0) { var parent = queue.Dequeue(); foreach (var (pid, ppid) in entries) if (ppid == parent && pids.Add(pid)) queue.Enqueue(pid); }
        }
        finally { CloseHandle(snapshot); }
        return pids;
    }

    private static void HideWindowsForPids(HashSet<uint> pids)
    {
        EnumWindows((hWnd, _) => { if (IsWindowVisible(hWnd)) { GetWindowThreadProcessId(hWnd, out uint pid); if (pids.Contains(pid)) ShowWindow(hWnd, SW_HIDE); } return true; }, IntPtr.Zero);
    }

    private void InstallWindowHook()
    {
        if (_winEventHook != IntPtr.Zero) return;

        _winEventCallback = (hHook, eventType, hwnd, idObject, idChild, idThread, dwms) =>
        {
            if (idObject != 0 || idChild != 0) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            var tracked = _trackedPids;
            if (tracked.Contains(pid))
            {
                ShowWindow(hwnd, SW_HIDE);
                return;
            }

            var className = new StringBuilder(64);
            if (GetClassName(hwnd, className, 64) > 0 && className.ToString() == "ConsoleWindowClass")
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (proc.ProcessName.Equals("winws", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowWindow(hwnd, SW_HIDE);
                        _trackedPids = new HashSet<uint>(tracked) { pid };
                    }
                }
                catch { }
            }
        };

        _winEventHook = SetWinEventHook(
            EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
            IntPtr.Zero, _winEventCallback,
            0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void RemoveWindowHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventCallback = null;
    }

    public void Cleanup()
    {
        // Останавливаем оркестратор
        if (_orchestrator.IsRunning)
            _orchestrator.Stop();

        // Останавливаем таймеры
        _uptimeTimer?.Stop();
        _orchestratorUiTimer?.Stop();

        // Убираем хук и отменяем отслеживание
        RemoveWindowHook();
        _hideWindowsCts?.Cancel();
        _hideWindowsCts?.Dispose();
    }
}
