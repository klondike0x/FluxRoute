using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Updater.Services;
using FluxRoute.Views;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace FluxRoute.ViewModels;

/// <summary>
/// Feature ViewModel для вкладки "Сервис".
/// Изолирует логику Game Filter, IPSet и zapret-service от MainViewModel.
/// </summary>
public sealed partial class ServiceViewModel : ObservableObject
{
    private readonly Func<string> _getEngineDir;
    private readonly Func<string?> _getSelectedProfileDisplayName;
    private readonly Action<string> _addAppLog;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectivityChecker _connectivityChecker;
    private readonly ILogger<ServiceViewModel>? _logger;

    // ═══ v1.6.0 (#60): Fallback-зеркала для IPSet и Hosts ═══
    private static readonly string[] IpSetFallbackUrls =
    {
        MirrorUrls.IpSetList,
        MirrorUrls.IpSetListMirrorSf, // SourceForge-зеркало
    };

    private static readonly string[] HostsFallbackUrls =
    {
        MirrorUrls.HostsFile,
        MirrorUrls.HostsFileMirrorSf, // SourceForge-зеркало
    };
    // ════════════════════════════════════════════════════════════

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

    public ObservableCollection<string> ServiceLogs { get; } = new();
    public List<string> GameFilterProtocols { get; } = ["TCP и UDP", "TCP", "UDP"];

    [ObservableProperty] private bool gameFilterEnabled;
    [ObservableProperty] private string gameFilterProtocol = "TCP и UDP";
    [ObservableProperty] private string ipSetMode = "—";
    [ObservableProperty] private string zapretServiceStatus = "—";
    [ObservableProperty] private bool isServiceBusy;

    private string EngineDir => _getEngineDir();
    private string GameFilterFlagPath => Path.Combine(EngineDir, "utils", "game_filter.enabled");
    private string IpSetFilePath => Path.Combine(EngineDir, "lists", "ipset-all.txt");
    private string IpSetBackupPath => Path.Combine(EngineDir, "lists", "ipset-all.txt.backup");

    partial void OnGameFilterProtocolChanged(string value)
    {
        if (GameFilterEnabled)
        {
            try
            {
                var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
                Directory.CreateDirectory(utilsDir);
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(value));
                AddLog($"🎮 Game Filter протокол изменён на {value}");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка обновления Game Filter: {ex.Message}");
            }
        }
    }

    public ServiceViewModel(
        Func<string> getEngineDir,
        Func<string?> getSelectedProfileDisplayName,
        Action<string> addAppLog,
        IHttpClientFactory httpClientFactory,
        IConnectivityChecker connectivityChecker,
        ILogger<ServiceViewModel>? logger = null) // ═══ v1.6.0: ILogger (опционально для бэквард-совместимости) ═══
    {
        _getEngineDir = getEngineDir;
        _getSelectedProfileDisplayName = getSelectedProfileDisplayName;
        _addAppLog = addAppLog;
        _httpClientFactory = httpClientFactory;
        _connectivityChecker = connectivityChecker;
        _logger = logger;
    }

    private string ProtocolToFileValue(string protocol) =>
        _protocolToFile.TryGetValue(protocol, out var v) ? v : "udp";

    private string FileValueToProtocol(string fileValue) =>
        _fileToProtocol.TryGetValue(fileValue, out var v) ? v : "UDP";

    private void AddLog(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            ServiceLogs.Add(msg);
            while (ServiceLogs.Count > 50) ServiceLogs.RemoveAt(0);
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ServiceLogs.Add(msg);
                while (ServiceLogs.Count > 50) ServiceLogs.RemoveAt(0);
            });
        }
    }

    public void Refresh()
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
                if (lines.Length == 0) IpSetMode = "any";
                else if (lines.Length == 1 && lines[0].Trim() == "203.0.113.113/32") IpSetMode = "none";
                else IpSetMode = "loaded";
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
                AddLog("🎮 Game Filter выключен");
                _addAppLog("Game Filter выключен");
            }
            else
            {
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(GameFilterProtocol));
                GameFilterEnabled = true;
                AddLog($"🎮 Game Filter включён ({GameFilterProtocol})");
                _addAppLog($"Game Filter включён ({GameFilterProtocol})");
            }
            AddLog("⚠️ Перезапустите zapret для применения изменений");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
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
                if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                if (File.Exists(IpSetFilePath)) File.Move(IpSetFilePath, IpSetBackupPath);
                File.WriteAllText(IpSetFilePath, "203.0.113.113/32\r\n");
                IpSetMode = "none";
                AddLog("🔒 IPSet → none (фильтрация отключена)");
            }
            else if (IpSetMode == "none")
            {
                File.WriteAllText(IpSetFilePath, "");
                IpSetMode = "any";
                AddLog("🌐 IPSet → any (все адреса)");
            }
            else
            {
                if (File.Exists(IpSetBackupPath))
                {
                    if (File.Exists(IpSetFilePath)) File.Delete(IpSetFilePath);
                    File.Move(IpSetBackupPath, IpSetFilePath);
                    IpSetMode = "loaded";
                    AddLog("📋 IPSet → loaded (список восстановлен)");
                }
                else
                {
                    AddLog("⚠️ Нет бэкапа IPSet. Обновите список через кнопку ниже");
                    return;
                }
            }
            AddLog("⚠️ Перезапустите zapret для применения изменений");
            Refresh();
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateIpSetList()
    {
        IsServiceBusy = true;
        AddLog("⬇️ Скачиваем ipset-all.txt...");
        try
        {
            // ═══ v1.6.0 (#60): Пробуем основной URL → fallback-зеркала ═══
            var content = await TryFetchFromMirrorsAsync(IpSetFallbackUrls, "ipset-all.txt");
            if (content is null)
            {
                AddLog("❌ Не удалось скачать IPSet ни с одного источника");
                return;
            }
            // ═══════════════════════════════════════════════════════════

            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);
            await File.WriteAllTextAsync(IpSetFilePath, content);
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"✅ IPSet обновлён ({content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} записей)");
                Refresh();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"❌ Ошибка скачивания IPSet: {ex.Message}");
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
        AddLog("⬇️ Проверяем hosts файл...");
        try
        {
            // ═══ v1.6.0 (#60): Пробуем основной URL → fallback-зеркала ═══
            var newContent = await TryFetchFromMirrorsAsync(HostsFallbackUrls, "hosts");
            if (newContent is null)
            {
                AddLog("❌ Не удалось скачать hosts ни с одного источника");
                return;
            }
            // ═══════════════════════════════════════════════════════════

            var newLines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (!File.Exists(hostsPath))
            {
                Application.Current.Dispatcher.Invoke(() => AddLog("❌ Файл hosts не найден"));
                return;
            }
            var currentHosts = await File.ReadAllTextAsync(hostsPath);
            var firstLine = newLines.FirstOrDefault()?.Trim() ?? "";
            var lastLine = newLines.LastOrDefault()?.Trim() ?? "";
            if (currentHosts.Contains(firstLine) && currentHosts.Contains(lastLine))
            {
                Application.Current.Dispatcher.Invoke(() => AddLog("✅ Hosts файл актуален"));
                return;
            }
            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, currentHosts + "\n" + newContent);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c copy /Y \"{tempPath}\" \"{hostsPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            });
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"✅ Hosts обновлён ({newLines.Length} записей добавлено)");
                Refresh();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"❌ Ошибка обновления hosts: {ex.Message}");
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private void InstallZapretService()
    {
        var profileName = _getSelectedProfileDisplayName();
        if (profileName is null)
        {
            AddLog("❌ Сначала выберите стратегию");
            return;
        }
        AddLog($"🔧 Установка службы zapret со стратегией «{profileName}»...");
        AddLog("⚠️ Запускаем service.bat — следуйте инструкциям в консоли");
        try
        {
            var serviceBat = Path.Combine(EngineDir, "service.bat");
            if (!File.Exists(serviceBat))
            {
                AddLog("❌ service.bat не найден в engine/");
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
            AddLog("✅ service.bat запущен с правами администратора");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForceStopZapretService()
    {
        if (!CustomDialog.Show(
            "⚠️ Подтверждение остановки",
            "Вы действительно хотите принудительно остановить службу zapret?\nВсе активные соединения через zapret будут прерваны.",
            "Остановить", "Отмена", isDanger: true)) return;

        AddLog("⏹ Принудительная остановка службы zapret...");
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
            AddLog("✅ Команды остановки отправлены");
            _ = Task.Delay(3000).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(Refresh));
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshServiceInfo()
    {
        Refresh();
        AddLog("🔄 Статус обновлён");
    }

    /// <summary>
    /// Показывает диалог с пояснением работы Auto-Tune.
    /// </summary>
    [RelayCommand]
    private void OpenAutoTuneHelp()
    {
        CustomDialog.Show(
            "Что такое Auto-Tune?",
            "Auto-Tune автоматически проверяет комбинации IPSet (какие IP-адреса обрабатывать) и GameFilter (какие порты фильтровать).\n\n" +
            "Программа запускает каждую комбинацию на 4 секунды, проверяет доступность YouTube, Discord и других сайтов, и выбирает лучшую.\n\n" +
            "Подробнее — в README.",
            "OK", "");
    }

    /// <summary>
    /// Применяет состояние GameFilter + IPSet без перезапуска zapret.
    /// Перезапуск запускается вызывающей стороной (MainViewModel.ApplyPreset).
    /// </summary>
    public void ApplyPresetState(bool gameFilterEnabled, string protocol, string ipSetMode)
    {
        try
        {
            var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
            Directory.CreateDirectory(utilsDir);
            if (gameFilterEnabled)
            {
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(protocol));
                GameFilterEnabled = true;
                GameFilterProtocol = protocol;
                AddLog($"🎮 Game Filter → включён ({protocol})");
            }
            else
            {
                if (File.Exists(GameFilterFlagPath)) File.Delete(GameFilterFlagPath);
                GameFilterEnabled = false;
                AddLog("🎮 Game Filter → выключен");
            }
        }
        catch (Exception ex) { AddLog($"❌ GameFilter: {ex.Message}"); }

        try
        {
            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);
            var currentMode = IpSetMode;
            if (ipSetMode == currentMode)
            {
                AddLog($"📋 IPSet уже в режиме «{ipSetMode}», пропуск");
                return;
            }
            if (ipSetMode == "any")
            {
                if (File.Exists(IpSetFilePath) && currentMode == "loaded")
                {
                    if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                    File.Copy(IpSetFilePath, IpSetBackupPath);
                }
                File.WriteAllText(IpSetFilePath, "");
                IpSetMode = "any";
                AddLog("🌐 IPSet → any (все адреса)");
            }
            else if (ipSetMode == "loaded")
            {
                if (File.Exists(IpSetBackupPath))
                {
                    if (File.Exists(IpSetFilePath)) File.Delete(IpSetFilePath);
                    File.Copy(IpSetBackupPath, IpSetFilePath);
                    IpSetMode = "loaded";
                    AddLog("📋 IPSet → loaded (список восстановлен)");
                }
                else
                    AddLog("⚠️ IPSet backup не найден — используйте «Обновить IPSet» для загрузки списка");
            }
            else if (ipSetMode == "none")
            {
                if (File.Exists(IpSetFilePath) && currentMode == "loaded")
                {
                    if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                    File.Copy(IpSetFilePath, IpSetBackupPath);
                }
                File.WriteAllText(IpSetFilePath, "203.0.113.113/32\r\n");
                IpSetMode = "none";
                AddLog("🔒 IPSet → none");
            }
        }
        catch (Exception ex) { AddLog($"❌ IPSet: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════
    //  AUTO-TUNE: подбор лучшей комбинации IPSet × GameFilter
    // ══════════════════════════════════════════════════════════════

    private readonly ObservableCollection<AutoTuneResult> _autoTuneResults = new();
    public ObservableCollection<AutoTuneResult> AutoTuneResults => _autoTuneResults;

    private bool _autoTuneRunning;
    public bool AutoTuneRunning { get => _autoTuneRunning; set => SetProperty(ref _autoTuneRunning, value); }

    private bool _autoTuneOverlayVisible;
    public bool AutoTuneOverlayVisible { get => _autoTuneOverlayVisible; set => SetProperty(ref _autoTuneOverlayVisible, value); }

    private double _autoTuneProgress;
    public double AutoTuneProgress { get => _autoTuneProgress; set => SetProperty(ref _autoTuneProgress, value); }

    private string _autoTuneStatusText = "";
    public string AutoTuneStatusText { get => _autoTuneStatusText; set => SetProperty(ref _autoTuneStatusText, value); }

    private bool _autoTuneResultVisible;
    public bool AutoTuneResultVisible { get => _autoTuneResultVisible; set => SetProperty(ref _autoTuneResultVisible, value); }

    private string _bestIpSet = "";
    public string BestIpSet { get => _bestIpSet; set => SetProperty(ref _bestIpSet, value); }

    private string _bestProtocol = "";
    public string BestProtocol { get => _bestProtocol; set => SetProperty(ref _bestProtocol, value); }

    private int _bestSuccessCount;
    public int BestSuccessCount { get => _bestSuccessCount; set => SetProperty(ref _bestSuccessCount, value); }

    private int _bestTotalCount;
    public int BestTotalCount { get => _bestTotalCount; set => SetProperty(ref _bestTotalCount, value); }

    private double _bestTimeMs;
    public double BestTimeMs { get => _bestTimeMs; set => SetProperty(ref _bestTimeMs, value); }

    public Func<IEnumerable<FluxRoute.Core.Models.TargetEntry>>? GetAutoTuneTargets { get; set; }

    // Колбэки для управления глобальным оверлеем (устанавливаются MainViewModel)
    public Action<string, object, ICommand?>? RequestShowOverlay { get; set; }
    public Action? RequestHideOverlay { get; set; }

    private CancellationTokenSource? _autoTuneCts;

    private volatile bool _isAutoTuneTaskRunning;

    [RelayCommand]
    private async Task StartAutoTune()
    {
        Log.Information("Auto-Tune: StartAutoTune вызван");

        // Ждём, пока завершится предыдущая задача
        while (_isAutoTuneTaskRunning)
            await Task.Delay(100);

        // Сбрасываем состояния
        AutoTuneResultVisible = false;
        AutoTuneProgress = 0;
        AutoTuneStatusText = "Подготовка...";

        // Показываем глобальный оверлей
        var content = new Controls.AutoTuneProgressView { DataContext = this };
        RequestShowOverlay?.Invoke("Подобрать настройки", content, CloseAutoTuneCommand);

        _autoTuneCts = new CancellationTokenSource();
        Log.Information("Auto-Tune: _autoTuneCts создан, запуск RunAutoTuneAsync");
        _ = RunAutoTuneAsync(_autoTuneCts.Token);
    }

    [RelayCommand]
    private void CancelAutoTune()
    {
        Log.Information("Auto-Tune: CancelAutoTune вызван");
        _autoTuneCts?.Cancel();
        AddLog("⚠️ Auto-Tune отменён пользователем");
        RequestHideOverlay?.Invoke();
    }

    [RelayCommand]
    private void CloseAutoTune()
    {
        Log.Information("Auto-Tune: CloseAutoTune вызван");
        _autoTuneCts?.Cancel();
        RequestHideOverlay?.Invoke();
    }

    [RelayCommand]
    private void ApplyBestAutoTune()
    {
        ApplyPresetState(!string.IsNullOrEmpty(BestProtocol) && BestProtocol != "Выкл",
            BestProtocol == "Выкл" ? "TCP и UDP" : BestProtocol,
            BestIpSet);
        RequestHideOverlay?.Invoke();
        AddLog($"✅ Лучшая конфигурация применена: IPSet={BestIpSet}, GameFilter={BestProtocol}");
        Log.Information("Auto-Tune: лучшая конфигурация применена: IPSet={IpSet}, GameFilter={Protocol}", BestIpSet, BestProtocol);
    }

    /// <summary>
    /// Основной цикл Auto-Tune с надёжной обработкой ошибок и таймаутов.
    /// </summary>
    private async Task RunAutoTuneAsync(CancellationToken ct)
    {
        Log.Information("Auto-Tune: RunAutoTuneAsync вход");
        _isAutoTuneTaskRunning = true;

        bool wasCancelled = false;
        bool hadResults = false;
        List<AutoTuneResult> results = new();

        try
        {
            // Шаг 1: Инициализация UI
            Log.Information("Auto-Tune: инициализация UI через Dispatcher");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AutoTuneResultVisible = false;
                AutoTuneProgress = 0;
                AutoTuneStatusText = "Подготовка...";
                _autoTuneResults.Clear();
            });

            // Шаг 2: Проверка токена и инициализация checker
            if (ct.IsCancellationRequested)
            {
                Log.Warning("Auto-Tune: токен отменён до старта");
                wasCancelled = true;
                return;
            }

            Log.Information("Auto-Tune: ConnectivityChecker получен из DI, тип={Type}", _connectivityChecker.GetType().Name);

            var combos = new[]
            {
                ("loaded", "TCP и UDP"), ("loaded", "Выкл"),
                ("none",   "TCP и UDP"), ("none",   "Выкл"),
                ("any",    "TCP и UDP"), ("loaded", "TCP"),
                ("loaded", "UDP"),       ("none",   "TCP"),
                ("none",   "UDP"),       ("any",    "TCP"),
                ("any",    "UDP"),       ("any",    "Выкл"),
            };
            int total = combos.Length;
            Log.Information("Auto-Tune: всего комбинаций={Total}", total);

            // Шаг 3: Сохраняем исходные настройки
            Log.Information("Auto-Tune: сохранение исходных настроек");
            string origIpSet = "";
            bool origGfEnabled = false;
            string origProtocol = "";
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                origIpSet = IpSetMode;
                origGfEnabled = GameFilterEnabled;
                origProtocol = GameFilterProtocol;
                Log.Information("Auto-Tune: исходные настройки: IPSet={IpSet}, GF={GfEnabled}, Proto={Protocol}",
                    origIpSet, origGfEnabled, origProtocol);
            });

            // Шаг 4: Цели для проверки
            var targets = GetAutoTuneTargets?.Invoke()?.Take(5).ToList()
                ?? FluxRoute.Core.Services.ConnectivityChecker.BuiltinSites
                    .SelectMany(kv => kv.Value).Take(5).ToList();

            Log.Information("Auto-Tune: целей для проверки={Count}", targets.Count);
            foreach (var t in targets)
                Log.Debug("Auto-Tune: цель {Key} ({Kind}) = {Value}", t.Key, t.Kind, t.Value);

            bool foundPerfect = false;

            try
            {
                for (int i = 0; i < combos.Length && !foundPerfect; i++)
                {
                    // Шаг 5: Проверка отмены перед каждой комбинацией
                    if (ct.IsCancellationRequested)
                    {
                        Log.Information("Auto-Tune: отмена на комбинации {I}", i + 1);
                        wasCancelled = true;
                        break;
                    }

                    var (ip, pr) = combos[i];
                    var comboName = $"{ip} / {(pr == "Выкл" ? "без фильтра" : pr)}";
                    Log.Information("Auto-Tune: комбинация {I}/{Total}: {Combo}", i + 1, total, comboName);

                    // Шаг 6: Обновление UI — статус проверки
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AutoTuneStatusText = $"Проверка комбинации {i + 1} из {total}: IPSet={ip}, GameFilter={pr}...";
                        AutoTuneProgress = (double)(i + 1) / total * 100;
                    });

                    // Шаг 7: Применение настроек комбинации (с защитой от исключений)
                    bool gfEnabled = pr != "Выкл";
                    try
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            ApplyPresetState(gfEnabled, gfEnabled ? pr : "TCP и UDP", ip));
                        Log.Debug("Auto-Tune: ApplyPresetState выполнен: GF={Gf}, Proto={Proto}, IPSet={IpSet}",
                            gfEnabled, gfEnabled ? pr : "TCP и UDP", ip);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        Log.Error(ex, "Auto-Tune: ApplyPresetState упал на комбинации {Combo}", comboName);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            AddLog($"⚠️ Ошибка применения настроек для {comboName}: {ex.Message}"));
                        continue; // Пробуем следующую комбинацию
                    }

                    // Шаг 8: Пауза для применения настроек (с учётом отмены)
                    try { await Task.Delay(400, ct); }
                    catch (OperationCanceledException)
                    {
                        Log.Information("Auto-Tune: отмена во время паузы на комбинации {I}", i + 1);
                        wasCancelled = true; break;
                    }

                    if (ct.IsCancellationRequested) { wasCancelled = true; break; }
                    if (targets.Count == 0)
                    {
                        Log.Warning("Auto-Tune: цели отсутствуют, пропуск комбинации");
                        continue;
                    }

                    // Шаг 9: Запуск проверки с жёстким таймаутом
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    // Показываем промежуточный статус — что проверка идёт
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        AutoTuneStatusText = $"Проверка комбинации {i + 1} из {total}: IPSet={ip}, GameFilter={pr} (идет тест {targets.Count} целей, до 5 сек)...");

                    checkCts.CancelAfter(TimeSpan.FromSeconds(5));

                    IReadOnlyList<FluxRoute.Core.Models.CheckResult> checkResults;
                    try
                    {
                        Log.Debug("Auto-Tune: CheckAllAsync запуск для {Combo}, целей={Count}", comboName, targets.Count);

                        // Жёсткий таймаут через Task.WhenAny — гарантирует, что цикл не зависнет
                        var checkTask = _connectivityChecker.CheckAllAsync(targets, checkCts.Token);
                        var hardTimeoutTask = Task.Delay(TimeSpan.FromSeconds(6), ct);
                        var completed = await Task.WhenAny(checkTask, hardTimeoutTask).ConfigureAwait(false);

                        if (completed == hardTimeoutTask)
                        {
                            Log.Warning("Auto-Tune: жёсткий таймаут 6с для {Combo}", comboName);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                                AddLog($"⚠️ Таймаут проверки для {comboName} (6 сек)"));
                            checkResults = Array.Empty<FluxRoute.Core.Models.CheckResult>();
                        }
                        else
                        {
                            var (_, r) = await checkTask.ConfigureAwait(false);
                            checkResults = r;
                            Log.Information("Auto-Tune: CheckAllAsync завершён для {Combo}: {OkCount}/{TotalCount}",
                                comboName, checkResults.Count(rr => rr.Ok), checkResults.Count);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        Log.Information("Auto-Tune: отмена во время проверки {Combo}", comboName);
                        wasCancelled = true; break;
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("Auto-Tune: таймаут или отмена CheckAllAsync для {Combo}", comboName);
                        checkResults = Array.Empty<FluxRoute.Core.Models.CheckResult>();
                    }
                    catch (HttpRequestException ex)
                    {
                        Log.Error(ex, "Auto-Tune: HTTP-ошибка при проверке {Combo}", comboName);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            AddLog($"⚠️ HTTP-ошибка для {comboName}: {ex.Message}"));
                        checkResults = Array.Empty<FluxRoute.Core.Models.CheckResult>();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Auto-Tune: неожиданная ошибка при проверке {Combo}", comboName);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            AddLog($"❌ Ошибка проверки {comboName}: {ex.Message}"));
                        checkResults = Array.Empty<FluxRoute.Core.Models.CheckResult>();
                    }
                    sw.Stop();

                    int successCount = checkResults.Count(r => r.Ok);
                    var latencies = checkResults
                        .Where(r => r.Ok && r.ElapsedMs.HasValue)
                        .Select(r => r.ElapsedMs!.Value)
                        .ToList();

                    var result = new AutoTuneResult
                    {
                        IpSetMode = ip,
                        GameFilterProtocol = pr,
                        SuccessCount = successCount,
                        TotalCount = checkResults.Count,
                        AvgLatencyMs = latencies.Count > 0 ? latencies.Average() : 0,
                        MinLatencyMs = latencies.Count > 0 ? latencies.Min() : 0,
                        MaxLatencyMs = latencies.Count > 0 ? latencies.Max() : 0,
                        TestDuration = sw.Elapsed
                    };
                    results.Add(result);
                    Log.Information("Auto-Tune: результат {Combo}: {Success}/{Total} ({Rate:0.#}%), задержка={Latency:0}мс, время={Elapsed}s",
                        comboName, successCount, checkResults.Count, result.SuccessRate, result.AvgLatencyMs, sw.Elapsed.TotalSeconds);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _autoTuneResults.Add(result);
                        AutoTuneStatusText = $"Результат: {successCount}/{checkResults.Count} ({result.SuccessRate:0.#}%), средняя задержка {result.AvgLatencyMs:0} мс";
                    });

                    if (result.IsPerfect && result.AvgLatencyMs < 1000)
                    {
                        foundPerfect = true;
                        Log.Information("Auto-Tune: найдена идеальная комбинация: {Combo}", comboName);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            AutoTuneStatusText = $"🎯 Найдена идеальная комбинация: {comboName}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Information("Auto-Tune: отменено пользователем во время цикла");
                wasCancelled = true;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    AutoTuneStatusText = "⚠️ Отменено пользователем");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-Tune: неожиданная ошибка в цикле проверки");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AutoTuneStatusText = "❌ Ошибка при выполнении Auto-Tune";
                    AddLog($"❌ Критическая ошибка Auto-Tune: {ex.Message}");
                });
                // Продолжаем — finally закроет оверлей
            }

            // Шаг 10: Восстановление настроек (если не было отмены)
            if (!wasCancelled && !ct.IsCancellationRequested)
            {
                Log.Information("Auto-Tune: восстановление исходных настроек");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ApplyPresetState(origGfEnabled, origProtocol, origIpSet);
                        Log.Information("Auto-Tune: настройки восстановлены: GF={Gf}, Proto={Proto}, IPSet={IpSet}",
                            origGfEnabled, origProtocol, origIpSet);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Auto-Tune: ошибка восстановления настроек");
                        AddLog($"⚠️ Ошибка восстановления настроек: {ex.Message}");
                    }
                    AutoTuneProgress = 100;
                });

                if (results.Count > 0)
                    hadResults = true;
            }

            // Шаг 11: Показ результатов
            if (!wasCancelled && !ct.IsCancellationRequested && hadResults)
            {
                var best = results.OrderByDescending(r => r.CompositeScore).First();
                Log.Information("Auto-Tune: лучшая комбинация: IPSet={IpSet}, GF={Proto}, Score={Score}",
                    best.IpSetMode, best.GameFilterProtocol, best.CompositeScore);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AutoTuneResults.Clear();
                    foreach (var r in results.OrderByDescending(r => r.CompositeScore))
                        AutoTuneResults.Add(r);

                    BestIpSet = best.IpSetMode;
                    BestProtocol = best.GameFilterProtocol;
                    BestSuccessCount = best.SuccessCount;
                    BestTotalCount = best.TotalCount;
                    BestTimeMs = best.AvgLatencyMs;

                    AutoTuneStatusText = best.IsPerfect
                        ? "✅ Найдена идеальная комбинация!"
                        : "🏆 Лучшая комбинация найдена";
                    AutoTuneResultVisible = true;
                });
            }
        }
        finally
        {
            Log.Information("Auto-Tune: завершение, _isAutoTuneTaskRunning=false");
            _isAutoTuneTaskRunning = false;
            // Не закрываем оверлей, если есть результаты для показа
            if (!hadResults)
                RequestHideOverlay?.Invoke();
        }
    }

    // ═══ v1.6.0 (#60): Fallback-хелпер для скачивания через цепочку URL ═══
    /// <summary>
    /// Пробует скачать контент по цепочке URL (основной → зеркала).
    /// Логирует результаты через AddLog и ILogger (если доступен).
    /// </summary>
    private async Task<string?> TryFetchFromMirrorsAsync(string[] urls, string resourceName)
    {
        using var http = _httpClientFactory.CreateClient("Service");

        for (var i = 0; i < urls.Length; i++)
        {
            var url = urls[i];
            var label = i == 0 ? "основной источник" : $"зеркало #{i}";

            try
            {
                _logger?.LogDebug("Попытка {Attempt}/{Total} ({Label}): {Url} — {Resource}",
                    i + 1, urls.Length, label, url, resourceName);

                var content = await http.GetStringAsync(url);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (i > 0)
                        AddLog($"📡 {resourceName} загружен через {label}");
                    _logger?.LogInformation("{Resource}: загружено через {Label} ({Index}/{Total})",
                        resourceName, label, i + 1, urls.Length);
                    return content;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "{Resource}: {Label} недоступен — {Url}",
                    resourceName, label, url);

                // Логируем в UI только первое и последнее падение для чистоты лога
                if (i == 0)
                    AddLog($"⚠️ {resourceName}: основной источник недоступен, пробуем зеркала...");
                else if (i == urls.Length - 1)
                    AddLog($"❌ {resourceName}: все источники (1 + {urls.Length - 1} зеркал) недоступны");
            }
        }

        return null;
    }
    // ═══════════════════════════════════════════════════════════════════
}