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

using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Updater.Services;

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
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private const int SW_HIDE = 0;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
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

    // ── Профиль ──
    public string SelectedScriptName => SelectedProfile?.FileName ?? "—";
    [ObservableProperty] private ProfileItem? selectedProfile;
    partial void OnSelectedProfileChanged(ProfileItem? value) { OnPropertyChanged(nameof(SelectedScriptName)); RunningScriptName = value?.FileName ?? "—"; }

    // ── Статус ──
    [ObservableProperty] private string statusText = "Не запущено";
    [ObservableProperty] private string updateText = "Обновления не проверялись";
    [ObservableProperty] private string runningScriptName = "—";
    [ObservableProperty] private string pidText = "—";
    [ObservableProperty] private string uptimeText = "—";
    public string AppVersion { get; } = GetAppVersion();

    // ── Компактный интерфейс ──
    [ObservableProperty] private bool isSettingsOpen = false;
    [ObservableProperty] private bool isRunning = false;
    [ObservableProperty] private bool isLogsVisible = false;
    [ObservableProperty] private string currentStrategy = "—";
    [ObservableProperty] private string uploadSpeed = "0.0";
    [ObservableProperty] private string downloadSpeed = "0.0";

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
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string scanProgressText = "";
    public string OrchestratorToggleLabel => OrchestratorRunning ? "Остановить оркестратор" : "Запустить оркестратор";
    partial void OnOrchestratorRunningChanged(bool value) => OnPropertyChanged(nameof(OrchestratorToggleLabel));

    // ── Настройки сайтов ──
    [ObservableProperty] private bool siteYouTube = true;
    [ObservableProperty] private bool siteDiscord = true;
    [ObservableProperty] private bool siteGoogle = true;
    [ObservableProperty] private bool siteTwitch = true;
    [ObservableProperty] private bool siteInstagram = true;

    private readonly OrchestratorService _orchestrator;
    private readonly DispatcherTimer _orchestratorUiTimer = new() { Interval = TimeSpan.FromSeconds(5) };

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
    private string EngineDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");

    private readonly UpdaterService _updater = new();

    // ── Обновления ──
    [ObservableProperty] private bool autoUpdateEnabled = false;
    [ObservableProperty] private string updateStatus = "Не проверялось";
    [ObservableProperty] private string currentEngineVersion = "—";
    [ObservableProperty] private bool isUpdating;
    private UpdateInfo? _pendingUpdate;

    public MainViewModel()
    {
        Logs.Add("Приложение запущено.");
        AddToRecentLogs("🚀 Приложение запущено");
        LoadProfiles();
        DisableNativeUpdateCheck();
        CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
        _ = CheckUpdatesOnStartupAsync();
        RefreshDiagnostics();

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

    // ── Компактный интерфейс: команды ──

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
        Application.Current.Dispatcher.Invoke(() =>
        {
            AddOrchestratorLog(e.Message);
            OrchestratorStatus = e.Message;

            if (e.Message.Contains("Сканирование завершено"))
            {
                var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
                ProfileScores.Clear();
                foreach (var s in sorted) ProfileScores.Add(s);
            }

            if (e.IsSwitched && e.NewProfile is not null)
            {
                var profile = Profiles.FirstOrDefault(p => p.DisplayName == e.NewProfile);
                if (profile is not null)
                {
                    SelectedProfile = profile;
                    CurrentStrategy = profile.DisplayName;
                    Logs.Add($"[Оркестратор] Переключено на «{profile.DisplayName}»");
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
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Stop();
            if (profile is not null) { SelectedProfile = profile; Start(); }
        });
    }

    private Task UpdateProfileScoreAsync(string fileName, int score)
    {
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
        _orchestrator.EnabledSites = sites;
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!AutoUpdateEnabled) return;

        var update = await _updater.CheckForUpdateAsync(EngineDir);
        if (update is null) return;

        _pendingUpdate = update;
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateStatus = $"Доступна новая версия: {update.Version}";

            var result = System.Windows.MessageBox.Show(
                $"Доступно обновление Flowseal zapret!\n\nВерсия: {update.Version}\n\nОбновить сейчас?",
                "Обновление доступно",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result == System.Windows.MessageBoxResult.Yes)
                _ = InstallUpdateAsync();
        });
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
    private void Start()
    {
        if (SelectedProfile is null) { Logs.Add("Профиль не выбран."); AddToRecentLogs("❌ Профиль не выбран"); return; }
        if (!File.Exists(SelectedProfile.FullPath)) { Logs.Add($"BAT не найден: {SelectedProfile.FullPath}"); AddToRecentLogs("❌ BAT не найден"); return; }

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
        _hideWindowsCts = new CancellationTokenSource();
        var ct = _hideWindowsCts.Token;

        Process? winws = null;
        for (int i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
            var rootPid = !cmdProcess.HasExited ? (uint)cmdProcess.Id : 0;
            var pids = rootPid > 0 ? GetProcessTreePids(rootPid) : new HashSet<uint>();

            try { var c = Process.GetProcessesByName("winws").FirstOrDefault(); if (c is not null) pids.Add((uint)c.Id); } catch { }
            _trackedPids = pids;
            HideWindowsForPids(pids);

            if (winws is null)
                try { winws = Process.GetProcessesByName("winws").FirstOrDefault(); } catch { }

            if (winws is not null) break;
        }

        if (winws is null) { Logs.Add("winws.exe не найден после запуска BAT."); AddToRecentLogs("❌ winws.exe не найден"); return; }

        Application.Current.Dispatcher.Invoke(() =>
        {
            _runningProcess = winws;
            PidText = winws.Id.ToString();
            IsRunning = true;
            Logs.Add($"winws.exe запущен, PID: {winws.Id}");
            AddToRecentLogs($"✅ Запущен (PID: {winws.Id})");
        });

        try { await winws.WaitForExitAsync(ct); } catch (OperationCanceledException) { }

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

    [RelayCommand]
    private void Stop()
    {
        _hideWindowsCts?.Cancel();

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
        var update = await _updater.CheckForUpdateAsync(EngineDir);

        if (update is null)
        {
            UpdateStatus = $"✅ Актуальная версия ({CurrentEngineVersion})";
            Logs.Add("Обновлений не найдено.");
            UpdateLogs.Add("✅ Обновлений не найдено");
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
            if (_pendingUpdate is null) return;
        }
        await InstallUpdateAsync();
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        IsUpdating = true;
        Stop();

        await _updater.InstallUpdateAsync(EngineDir, _pendingUpdate,
            msg => Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateStatus = msg;
                Logs.Add(msg);
                UpdateLogs.Add(msg);
            }));

        CurrentEngineVersion = _updater.GetLocalVersion(EngineDir);
        _pendingUpdate = null;
        IsUpdating = false;
        LoadProfiles();
    }

    [RelayCommand] private void ApplyProfile() { if (SelectedProfile is null) { Logs.Add("Профиль не выбран."); return; } Logs.Add($"Выбран профиль: {SelectedProfile.FileName}"); }
    [RelayCommand] private void CopyDiagnostics() { try { Clipboard.SetText(BuildDiagnosticsText()); Logs.Add("Диагностика скопирована."); } catch (Exception ex) { Logs.Add($"Ошибка: {ex.Message}"); } }

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
    private static string GetAppVersion() { var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(); return asm.GetName().Version?.ToString() ?? "—"; }

    private void LoadProfiles()
    {
        Profiles.Clear();
        if (!Directory.Exists(EngineDir)) { Logs.Add($"Папка engine не найдена: {EngineDir}"); SelectedProfile = null; return; }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat", "service,.bat" };
        var bats = Directory.EnumerateFiles(EngineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !excluded.Contains(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var bat in bats)
            Profiles.Add(new ProfileItem { FileName = Path.GetFileName(bat), DisplayName = Path.GetFileNameWithoutExtension(bat), FullPath = bat });

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
}
