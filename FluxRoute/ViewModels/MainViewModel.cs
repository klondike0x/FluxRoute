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
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
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

        // 1. Переключить стратегию
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

    /// <summary>
    /// Нормализует ввод домена: убирает пробелы, протоколы, www., завершающий слеш.
    /// </summary>
    private string NormalizeDomainInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        // Убираем пробелы в начале/конце
        input = input.Trim();

        // Удаляем протоколы (регистронезависимо)
        input = System.Text.RegularExpressions.Regex.Replace(input, @"^https?://", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Удаляем www. (регистронезависимо)
        input = System.Text.RegularExpressions.Regex.Replace(input, @"^www\.", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Убираем завершающий слеш
        input = input.TrimEnd('/');

        // Удаляем оставшиеся пробелы (лишние, если были)
        input = input.Trim();

        return input;
    }

    [RelayCommand]
    private void AddCustomDomain()
    {
        var input = NewSiteInput.Trim();
        if (string.IsNullOrEmpty(input))
        {
            NewSiteInput = "";
            return;
        }

        // Нормализуем ввод
        input = NormalizeDomainInput(input);
        if (string.IsNullOrEmpty(input))
        {
            NewSiteInput = "";
            return;
        }

        var list = SelectedTabMode == "Exclusions" ? CustomExcludeDomains : CustomTargetDomains;
        var modeName = SelectedTabMode == "Exclusions" ? "исключение" : "проверка";

        // Проверяем на дубликаты (регистронезависимо)
        if (list.Any(d => string.Equals(d, input, StringComparison.OrdinalIgnoreCase)))
        {
            // Домен уже существует — показываем ошибку через CustomDialog
            CustomDialog.Show(
                "⚠️ Дубликат домена",
                $"Домен «{input}» уже добавлен в список {modeName}.",
                "OK", isDanger: false);
            NewSiteInput = "";
            return;
        }

        list.Add(input);
        NewSiteInput = "";
        SaveSettings();
        SyncCustomHostlist();
        AddToRecentLogs($"✅ Добавлен домен: {input} ({modeName})");
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

    // ═══ v1.6.0: Feature #53 — Массовый импорт доменов ═══

    /// <summary>
    /// Парсит сырой текст и нормализует домены. Разделители: пробел, запятая, точка с запятой, перенос строки.
    /// </summary>
    private List<string> ParseAndNormalizeDomains(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        return System.Text.RegularExpressions.Regex.Split(raw, @"[\s,;]+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeDomainInput)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Добавляет только те домены, которых ещё нет в списке (регистронезависимая дедупликация).
    /// </summary>
    private int AddDomainsBulk(List<string> domains)
    {
        var list = SelectedTabMode == "Exclusions" ? CustomExcludeDomains : CustomTargetDomains;
        var existing = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var domain in domains)
        {
            if (existing.Add(domain))
            {
                list.Add(domain);
                added++;
            }
        }

        return added;
    }

    [RelayCommand]
    private void ImportDomainsFromClipboard()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                AddToRecentLogs("📋 Буфер обмена пуст или не содержит текст");
                return;
            }

            var raw = System.Windows.Clipboard.GetText();
            var domains = ParseAndNormalizeDomains(raw);

            if (domains.Count == 0)
            {
                AddToRecentLogs("❌ Не найдено доменов в буфере обмена");
                return;
            }

            var added = AddDomainsBulk(domains);
            SaveSettings();
            SyncCustomHostlist();
            AddToRecentLogs($"✅ Импортировано {added} домен(ов) из буфера обмена (пропущено дубликатов: {domains.Count - added})");
        }
        catch (Exception ex)
        {
            AddToRecentLogs($"❌ Ошибка импорта из буфера обмена: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportDomainsFromFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Выберите файл со списком доменов"
            };

            if (dialog.ShowDialog() != true)
                return;

            var raw = File.ReadAllText(dialog.FileName);
            var domains = ParseAndNormalizeDomains(raw);

            if (domains.Count == 0)
            {
                AddToRecentLogs($"❌ Не найдено доменов в файле: {dialog.FileName}");
                return;
            }

            var added = AddDomainsBulk(domains);
            SaveSettings();
            SyncCustomHostlist();
            AddToRecentLogs($"✅ Импортировано {added} домен(ов) из файла (пропущено дубликатов: {domains.Count - added})");
        }
        catch (Exception ex)
        {
            AddToRecentLogs($"❌ Ошибка импорта из файла: {ex.Message}");
        }
    }

    // ═══ v1.6.0: Feature #53 — Сброс рейтинга стратегий ═══

    [RelayCommand]
    private void ResetProfileRatings()
    {
        if (!CustomDialog.Show(
                "🔄 Сбросить рейтинг стратегий",
                "Очистить историю оценок оркестратора и ИИ? Это заставит программу заново просканировать все стратегии.",
                "Очистить",
                "Отмена",
                isDanger: true))
            return;

        ProfileScores.Clear();
        SaveSettings();
        _aiRegistry.ResetAll();
        AddToRecentLogs("🔄 Рейтинг стратегий и история ИИ сброшены.");
    }

    // ═══ v1.6.0: Feature #21 — Команда выхода из приложения ═══

    /// <summary>
    /// Принудительный выход из приложения (обход CloseToTray, всегда показывает подтверждение).
    /// </summary>
    [RelayCommand]
    private void ExitApplication()
    {
        if (CustomDialog.Show(
                "Завершить работу FluxRoute?",
                "Все активные службы (WinDivert, WinWS) будут остановлены, защита прекратит работу.",
                "Завершить",
                "Отмена",
                isDanger: true))
        {
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// Внешний метод для MainWindow — устанавливает флаг подтверждённого закрытия.
    /// Вызывается из OnTrayExitRequested.
    /// </summary>
    public void ConfirmClose() => Application.Current.Shutdown();

    // ── События ──
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenAboutRequested;
    public event EventHandler<string>? ProfileSwitchNotification;

    // ── Стратегия ──
    public string SelectedScriptName => SelectedProfile?.FileName ?? "—";
    [ObservableProperty] private ProfileItem? selectedProfile;

    partial void OnSelectedProfileChanged(ProfileItem? oldValue, ProfileItem? newValue)
    {
        if (!_suppressProfileWarning && _settingsLoaded && ShowProfileSwitchWarning
            && oldValue is not null && newValue is not null)
        {
            if (!CustomDialog.Show(
                "⚠️ Смена стратегии",
                "Изменение стратегии может повлиять на работу приложения и сетевые подключения. Продолжить?",
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

    // ═══ v1.6.0: Дефолтный профиль для триггеров ═══
    /// <summary>
    /// Имя файла дефолтного профиля, на который возвращаться после триггера.
    /// Если не задан (пусто), используется профиль, активный до срабатывания триггера.
    /// </summary>
    [ObservableProperty] private string? defaultProfileFileName;

    partial void OnDefaultProfileFileNameChanged(string? value)
    {
        SaveSettings();
        if (!string.IsNullOrEmpty(value))
        {
            AddToRecentLogs($"📌 Дефолтный профиль установлен: {value}");
        }
        else
        {
            AddToRecentLogs($"📌 Дефолтный профиль очищен");
        }
    }

    /// <summary>
    /// Устанавливает профиль как дефолтный для триггеров.
    /// </summary>
    [RelayCommand]
    private void SetDefaultProfile(ProfileItem? profile)
    {
        if (profile is null)
        {
            DefaultProfileFileName = null;
            return;
        }

        DefaultProfileFileName = profile.FileName;
    }

    /// <summary>
    /// Очищает дефолтный профиль.
    /// </summary>
    [RelayCommand]
    private void ClearDefaultProfile()
    {
        DefaultProfileFileName = null;
    }
    // ═════════════════════════════════════════════════

    // ── Статус ──
    [ObservableProperty] private string statusText = "Не запущено";
    [ObservableProperty] private string updateText = "Обновления не проверялись";
    [ObservableProperty] private string runningScriptName = "—";
    [ObservableProperty] private string pidText = "—";
    [ObservableProperty] private string uptimeText = "—";
    public string AppVersion { get; } = GetAppVersion();
    public string AppBuildDate { get; } = GetBuildDate();
    public string AppRuntime { get; } = $".NET {Environment.Version} ({RuntimeInformation.ProcessArchitecture})";
    public string AppLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluxRoute", "logs");
    public string AppLicense { get; } = "GNU General Public License v3.0";
    public string AppRepositoryUrl { get; } = "https://github.com/klondike0x/FluxRoute";

    // ── Навигация ──
    [ObservableProperty] private int selectedTabIndex = 0;
    public string SelectedTabName => MainNavigation.GetName(SelectedTabIndex);
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedTabName));
        if (value == 1) OnTgProxyTabActivated();
        if (value == 2)
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

    public int ActiveServicesCount =>
        (OrchestratorEnabled ? 1 : 0)
        + (TgProxyRunning ? 1 : 0)
        + (GameFilterEnabled ? 1 : 0);

    public string ActiveServicesSummary => $"{ActiveServicesCount} из 3 активны";
    public string PingSummary => "Нет данных";
    public string CompactNetworkSummary => PingSummary;
    public string TrafficSpeedSummary => $"↓ {DownloadSpeed}  ↑ {UploadSpeed}";

    public string MainActionButtonText => IsRunning ? "⏹ Остановить" : "▶ Запустить";
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(MainActionButtonText));
    partial void OnUploadSpeedChanged(string value) => OnPropertyChanged(nameof(TrafficSpeedSummary));
    partial void OnDownloadSpeedChanged(string value) => OnPropertyChanged(nameof(TrafficSpeedSummary));

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
        OnPropertyChanged(nameof(ProtectionMode));
        OnPropertyChanged(nameof(IsAutomaticProtectionMode));
        OnPropertyChanged(nameof(IsManualProtectionMode));
        OnPropertyChanged(nameof(ActiveServicesCount));
        OnPropertyChanged(nameof(ActiveServicesSummary));
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
    [ObservableProperty] private double scanProgressValue;

    // ── Глобальный модальный оверлей ──
    [ObservableProperty] private bool globalOverlayVisible;
    [ObservableProperty] private string globalOverlayTitle = "";
    [ObservableProperty] private object? globalOverlayContent;
    [ObservableProperty] private ICommand? globalOverlayCloseCommand;

    public string OrchestratorToggleLabel => OrchestratorRunning ? "Остановить оркестратор" : "Запустить оркестратор";
    partial void OnOrchestratorRunningChanged(bool value) => OnPropertyChanged(nameof(OrchestratorToggleLabel));

    /// <summary>Текущий режим управления защитой на главном экране.</summary>
    public ProtectionMode ProtectionMode =>
        ProtectionModePolicy.FromOrchestratorEnabled(OrchestratorEnabled);

    public bool IsAutomaticProtectionMode => ProtectionMode == ProtectionMode.Automatic;
    public bool IsManualProtectionMode => ProtectionMode == ProtectionMode.Manual;

    [RelayCommand]
    private void SelectAutomaticProtectionMode()
    {
        if (!OrchestratorEnabled)
            OrchestratorEnabled = true;
    }

    [RelayCommand]
    private void SelectManualProtectionMode()
    {
        if (OrchestratorEnabled)
            OrchestratorEnabled = false;
    }

    // ── Настройки сайтов ──
    [ObservableProperty] private bool siteYouTube = true;
    partial void OnSiteYouTubeChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteDiscord = true;
    partial void OnSiteDiscordChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteGoogle = true;
    partial void OnSiteGoogleChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteTwitch = true;
    partial void OnSiteTwitchChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteInstagram = true;
    partial void OnSiteInstagramChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteTelegram = true;
    partial void OnSiteTelegramChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }
    [ObservableProperty] private bool siteTikTok = true;
    partial void OnSiteTikTokChanged(bool value) { SaveSettings(); SyncOrchestratorSites(); }

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

    // ── Предупреждение при смене стратегии ──
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

    [ObservableProperty] private StartupWindowMode startupWindowMode = StartupWindowMode.Minimal;
    partial void OnStartupWindowModeChanged(StartupWindowMode value) => SaveSettings();

    public IReadOnlyList<StartupWindowMode> StartupWindowModes { get; } =
        [StartupWindowMode.Modern, StartupWindowMode.Minimal];

    // ═══ v1.6.0: Крестик сворачивает в трей ═══
    [ObservableProperty] private bool closeToTray = true;
    partial void OnCloseToTrayChanged(bool value) => SaveSettings();

    // ═══ v1.6.0: Автозапуск через Планировщик задач ═══
    [ObservableProperty] private bool taskSchedulerAutoStart;
    partial void OnTaskSchedulerAutoStartChanged(bool value)
    {
        if (_suppressTaskSchedulerChanged) return;
        var exePath = Environment.ProcessPath
            ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                if (value)
                    _taskScheduler.CreateTask(exePath, HighPriorityStartupEnabled, DelayedAutoStartSeconds);
                else
                    _taskScheduler.RemoveTask();

                AddToRecentLogs(value ? "✅ Задача автозапуска создана" : "🗑 Задача автозапуска удалена");
            }
            catch (Exception ex)
            {
                // ═══ v1.6.0: Диагностика автозапуска — показываем понятное сообщение ═══
                var message = ex.Message.Contains("Access", StringComparison.OrdinalIgnoreCase)
                    ? "Недостаточно прав для создания задачи в Планировщике. Запустите приложение от администратора."
                    : $"Ошибка Планировщика задач: {ex.Message}";
                Logs.Add($"❌ {message}");
                AddToRecentLogs($"❌ Ошибка автозапуска: {ex.Message}");

                CustomDialog.Show(
                    "❌ Ошибка автозапуска",
                    message,
                    "OK", isDanger: true);

                // Откатываем чекбокс
                _ = Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _suppressTaskSchedulerChanged = true;
                    TaskSchedulerAutoStart = false;
                    _suppressTaskSchedulerChanged = false;
                });
                // ═══════════════════════════════════════════════════════════
                return;
            }
        }
        SaveSettings();
    }
    private bool _suppressTaskSchedulerChanged;

    // ═══ v1.6.0: Высокий приоритет автозагрузки ═══
    [ObservableProperty] private bool highPriorityStartupEnabled;
    partial void OnHighPriorityStartupEnabledChanged(bool value)
    {
        SaveSettings();
        // Пересоздаём задачу с новым приоритетом, если она существует
        if (_settingsLoaded && TaskSchedulerAutoStart)
        {
            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                try
                {
                    _taskScheduler.CreateTask(exePath, value, DelayedAutoStartSeconds);
                    AddToRecentLogs(value
                        ? "⚡ Приоритет автозагрузки: высокий"
                        : "🔹 Приоритет автозагрузки: нормальный");
                }
                catch (Exception ex)
                {
                    Logs.Add($"❌ Ошибка изменения приоритета: {ex.Message}");
                }
            }
        }
    }

    // ═══ v1.6.0: Отложенный автозапуск ═══
    [ObservableProperty] private int delayedAutoStartSeconds = 30;
    partial void OnDelayedAutoStartSecondsChanged(int value)
    {
        // Валидация: не меньше 0 и не больше 300 (5 минут)
        if (value < 0) DelayedAutoStartSeconds = 0;
        if (value > 300) DelayedAutoStartSeconds = 300;
        SaveSettings();
        // Пересоздаём задачу с новой задержкой
        if (_settingsLoaded && TaskSchedulerAutoStart)
        {
            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                try
                {
                    _taskScheduler.CreateTask(exePath, HighPriorityStartupEnabled, DelayedAutoStartSeconds);
                    AddToRecentLogs($"⏱ Задержка автозапуска: {DelayedAutoStartSeconds} сек");
                }
                catch (Exception ex)
                {
                    Logs.Add($"❌ Ошибка изменения задержки: {ex.Message}");
                }
            }
        }
    }

    // ═══ v1.6.0: Автоматический запуск последнего профиля ═══
    [ObservableProperty] private bool autoLaunchProfile;
    partial void OnAutoLaunchProfileChanged(bool value) => SaveSettings();

    // ═══ v1.6.0: Синхронизация доменов с UI ═══
    [ObservableProperty] private bool syncDomainsWithUI = true;
    partial void OnSyncDomainsWithUIChanged(bool value) => SaveSettings();

    // ═══ v1.6.0: Поле ввода для подбора стратегии по домену/IP ═══
    [ObservableProperty] private string findBestStrategyTarget = "";

    // ── Обновления (wrappers → UpdatesViewModel) ──
    public string UpdateStatus => Updates.UpdateStatus;
    public string CurrentEngineVersion => Updates.CurrentEngineVersion;
    public bool IsUpdating => Updates.IsUpdating;
    public bool IsDownloadingEngine => Updates.IsDownloadingEngine;
    public string EngineDownloadStatus => Updates.EngineDownloadStatus;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITaskSchedulerService _taskScheduler;
    private readonly TrayIconService? _trayIcon;
    private readonly StrategyEvolver _evolver;

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
        BatMaterializer aiMaterializer,
        IHttpClientFactory httpClientFactory,
        ITaskSchedulerService? taskScheduler = null,
        TrayIconService? trayIcon = null)
    {
        _settingsService = settingsService;
        _updater = updaterService;
        _connectivity = connectivity;
        _appUpdater = appUpdaterService;
        _aiRegistry = aiRegistry;
        _aiHistoryStore = aiHistoryStore;
        _aiFingerprints = aiFingerprints;
        _httpClientFactory = httpClientFactory;
        _taskScheduler = taskScheduler ?? new TaskSchedulerService();
        _trayIcon = trayIcon;
        _evolver = aiEvolver;

        // ── Инициализация feature ViewModels ──
        Diagnostics = new DiagnosticsViewModel(
            getEngineDir: () => EngineDir,
            getWinwsPath: () => WinwsPath,
            getWinDivertDllPath: () => WinDivertDllPath,
            getWinDivertSys64Path: () => WinDivertSys64Path,
            getWinDivertSysPath: () => WinDivertSysPath,
            addAppLog: msg => Logs.Add(msg));

        // ═══ ИСПРАВЛЕНО: передаём httpClientFactory + connectivityChecker в ServiceViewModel ═══
        Service = new ServiceViewModel(
            getEngineDir: () => EngineDir,
            getSelectedProfileDisplayName: () => SelectedProfile?.DisplayName,
            addAppLog: msg => Logs.Add(msg),
            httpClientFactory: _httpClientFactory,
            connectivityChecker: _connectivity);
        // ════════════════════════════════════════════════════════════════════════════════════════

        // Показ/скрытие глобального оверлея из ServiceViewModel
        Service.RequestShowOverlay = (title, content, closeCmd) =>
        {
            GlobalOverlayTitle = title;
            GlobalOverlayContent = content;
            GlobalOverlayCloseCommand = closeCmd;
            GlobalOverlayVisible = true;
        };
        Service.RequestHideOverlay = () => GlobalOverlayVisible = false;

        Service.GetAutoTuneTargets = () =>
        {
            var sites = new List<string>();
            if (SiteYouTube) sites.Add("YouTube");
            if (SiteDiscord) sites.Add("Discord");
            if (SiteGoogle) sites.Add("Google");
            if (SiteTwitch) sites.Add("Twitch");
            if (SiteInstagram) sites.Add("Instagram");
            if (SiteTelegram) sites.Add("Telegram");
            if (SiteTikTok) sites.Add("TikTok");
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
        Service.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(ServiceViewModel.GameFilterEnabled))
            {
                OnPropertyChanged(nameof(ActiveServicesCount));
                OnPropertyChanged(nameof(ActiveServicesSummary));
            }
        };
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
            Logs.Add("📊 Рейтинг стратегий восстановлен.");
        }

        _settingsLoaded = true;

        if (!Directory.Exists(EngineDir) || Directory.GetFiles(EngineDir, "*.bat").Length == 0)
        {
            Logs.Add("⚠️ Папка engine/ не найдена. Скачиваем Flowseal...");
            AddToRecentLogs("⬇️ Скачивание Flowseal...");
            _ = Updates.AutoDownloadEngineAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.InnerException ?? t.Exception;
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        Logs.Add($"❌ Ошибка автоскачивания: {ex?.Message}");
                        AddToRecentLogs($"❌ Flowseal: {ex?.Message}");
                    });
                }
            }, TaskScheduler.Default);
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

        // ═══ v1.6.0: Автозапуск последнего профиля через 2.5 сек ═══
        if (AutoLaunchProfile && SelectedProfile is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (SelectedProfile is not null && !IsRunning)
                        {
                            Logs.Add($"🚀 Автозапуск профиля: {SelectedProfile.DisplayName}");
                            AddToRecentLogs($"🚀 Автозапуск профиля: {SelectedProfile.DisplayName}");
                            Start();
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Автозапуск не должен ломать основной запуск приложения
                    Trace.TraceError($"⚠ Ошибка автозапуска профиля: {ex.Message}");
                }
            });
        }
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
        SiteTikTok = settings.SiteTikTok;
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
        StartupWindowMode = settings.StartupWindowMode;
        // ═══ v1.6.0: Крестик сворачивает в трей ═══
        CloseToTray = settings.CloseToTray;
        // ═══════════════════════════════════════
        GameFilterProtocol = settings.GameFilterProtocol;
        ShowProfileSwitchWarning = settings.ShowProfileSwitchWarning;

        // ═══ v1.6.0 ═══
        TaskSchedulerAutoStart = settings.TaskSchedulerAutoStart;
        HighPriorityStartupEnabled = settings.HighPriorityStartupEnabled;
        DelayedAutoStartSeconds = settings.DelayedAutoStartSeconds > 0 ? settings.DelayedAutoStartSeconds : 30;
        AutoLaunchProfile = settings.AutoLaunchProfile;
        SyncDomainsWithUI = settings.SyncDomainsWithUI;
        DefaultProfileFileName = settings.DefaultProfileFileName; // Дефолтный профиль для триггеров

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
            DefaultProfileFileName = DefaultProfileFileName, // Дефолтный профиль для триггеров
            OrchestratorInterval = OrchestratorInterval,
            OrchestratorEnabled = OrchestratorEnabled,
            SiteYouTube = SiteYouTube,
            SiteDiscord = SiteDiscord,
            SiteGoogle = SiteGoogle,
            SiteTwitch = SiteTwitch,
            SiteInstagram = SiteInstagram,
            SiteTelegram = SiteTelegram,
            SiteTikTok = SiteTikTok,
            UserSites = UserCustomSitesText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),
            CustomTargetDomains = CustomTargetDomains.ToList(),
            CustomExcludeDomains = CustomExcludeDomains.ToList(),
            AutoUpdateEnabled = AutoUpdateEnabled,
            AutoStartEnabled = AutoStartEnabled,
            MinimizeToTray = MinimizeToTray,
            StartupWindowMode = StartupWindowMode,
            // ═══ v1.6.0: Крестик сворачивает в трей ═══
            CloseToTray = CloseToTray,
            // ═══════════════════════════════════════
            GameFilterProtocol = GameFilterProtocol,
            ShowProfileSwitchWarning = ShowProfileSwitchWarning,
            TaskSchedulerAutoStart = TaskSchedulerAutoStart,
            HighPriorityStartupEnabled = HighPriorityStartupEnabled,
            DelayedAutoStartSeconds = DelayedAutoStartSeconds,
            AutoLaunchProfile = AutoLaunchProfile,
            SyncDomainsWithUI = SyncDomainsWithUI,
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
    private void ShowLogs() => SelectedTabIndex = 5;

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
    private void SyncCustomHostlist()
    {
        // v1.6.0: Пропускаем синхронизацию, если пользователь её отключил
        if (!SyncDomainsWithUI)
            return;

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

            // Записываем list-general-user.txt
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

            // ═══ v1.6.0: Синхронизация list-exclude-user.txt ═══
            var excludeHostlistPath = Path.Combine(listsDir, "list-exclude-user.txt");
            var excludeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in CustomExcludeDomains)
            {
                if (!string.IsNullOrWhiteSpace(d))
                    excludeDomains.Add(d.Trim());
            }

            if (excludeDomains.Count > 0)
            {
                if (File.Exists(excludeHostlistPath))
                {
                    try { File.SetAttributes(excludeHostlistPath, FileAttributes.Normal); } catch { }
                }
                File.WriteAllLines(excludeHostlistPath, excludeDomains.OrderBy(x => x), new UTF8Encoding(false));
                Logs.Add($"[Sync] Записано {excludeDomains.Count} исключений в list-exclude-user.txt");
            }
            else
            {
                if (File.Exists(excludeHostlistPath))
                    File.Delete(excludeHostlistPath);
            }
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка синхронизации hostlist-файлов: {ex.Message}");
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
        _hideWindowsCts?.Cancel();
        _hideWindowsCts?.Dispose();
    }
}