using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;

namespace FluxRoute.ViewModels;

public partial class DohViewModel : ObservableObject
{
    private readonly IDohProviderService _providerService;
    private readonly IDohSelectionService _selectionService;
    private readonly IWindowsDohConfigurationService _configurationService;
    private readonly IDohProviderSwitchService _providerSwitchService;
    private readonly IDnsAdapterService _dnsAdapterService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<DohViewModel> _logger;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _modeApplyCts;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private Task _operationTask = Task.CompletedTask;
    private long _operationGeneration;
    private bool _isInitialized;
    private bool _suppressDohToggle;
    private DohProvider? _appliedProvider;
    private DohProvider? _activeProvider;
    private DohEncryptionMode _appliedMode;
    private readonly Func<string, IReadOnlyList<string>> _getDnsAddresses;

    public DohViewModel(
        IDohProviderService providerService,
        IDohSelectionService selectionService,
        IWindowsDohConfigurationService configurationService,
        IDohProviderSwitchService providerSwitchService,
        IDnsAdapterService dnsAdapterService,
        ISettingsService settingsService,
        ILogger<DohViewModel> logger,
        IReadOnlyList<string>? networkInterfaces = null,
        Func<string, IReadOnlyList<string>>? getDnsAddresses = null)
    {
        _providerService = providerService;
        _selectionService = selectionService;
        _configurationService = configurationService;
        _providerSwitchService = providerSwitchService;
        _dnsAdapterService = dnsAdapterService;
        _settingsService = settingsService;
        _logger = logger;
        _getDnsAddresses = getDnsAddresses ?? GetDnsAddresses;

        Providers = new ObservableCollection<DohProvider>(_providerService.Providers);
        NetworkInterfaces = new ObservableCollection<string>(networkInterfaces ?? GetActiveNetworkInterfaces());

        var settings = _settingsService.Load().Doh;
        AutomaticSelection = settings.AutomaticSelection;
        EncryptionMode = settings.EncryptionMode;
        SelectedInterface = NetworkInterfaces.FirstOrDefault(x => x == settings.InterfaceName)
            ?? NetworkInterfaces.FirstOrDefault();
        SelectedProvider = Providers.FirstOrDefault(x => x.Id == settings.SelectedProviderId)
            ?? Providers.FirstOrDefault();
        var savedAppliedProvider = settings.Enabled
            ? Providers.FirstOrDefault(x => x.Id == settings.AppliedProviderId)
            : null;
        _activeProvider = DetectActiveProvider();
        _appliedProvider = settings.Enabled
            ? _activeProvider ?? savedAppliedProvider
            : null;
        _appliedMode = settings.AppliedEncryptionMode;
        Status = settings.Enabled
            ? _activeProvider is not null
                ? $"Активный DNS: {_activeProvider.Name}"
                : savedAppliedProvider is not null
                    ? $"Сохранён провайдер: {savedAppliedProvider.Name}; активный DNS не определён"
                    : "DoH был включён в последней конфигурации"
            : "DoH не настроен";
        IsDohEnabled = settings.Enabled;
        _isInitialized = true;
    }

    public ObservableCollection<DohProvider> Providers { get; }
    public ObservableCollection<DohProviderTestResult> Results { get; } = new();
    public ObservableCollection<string> NetworkInterfaces { get; }

    public string ActiveProviderText => _activeProvider is null
        ? "Активный DNS: не определён"
        : $"Активный DNS: {_activeProvider.Name}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private DohProvider? selectedProvider;

    partial void OnSelectedProviderChanged(DohProvider? oldValue, DohProvider? newValue)
    {
        if (!_isInitialized)
            return;

        SaveDohPreferences();
        if (!IsDohEnabled
            || oldValue is null || newValue is null || oldValue.Id == newValue.Id
            || string.IsNullOrWhiteSpace(SelectedInterface))
        {
            return;
        }

        StartSerializedOperation((generation, ct) => SwitchProviderAutomaticallyAsync(oldValue, newValue, generation, ct));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
    private string? selectedInterface;

    partial void OnSelectedInterfaceChanged(string? value)
    {
        if (!_isInitialized) return;
        SaveDohPreferences();
        RefreshActiveProvider();
    }

    [ObservableProperty] private bool automaticSelection = true;

    partial void OnAutomaticSelectionChanged(bool value)
    {
        if (!_isInitialized) return;
        SaveDohPreferences();
    }
    [ObservableProperty] private DohEncryptionMode encryptionMode = DohEncryptionMode.EncryptedOnly;
    [ObservableProperty] private bool isDohEnabled;

    partial void OnIsDohEnabledChanged(bool value)
    {
        if (!_isInitialized || _suppressDohToggle)
        {
            return;
        }

        StartSerializedOperation(value
            ? ApplyEncryptionModeAutomaticallyAsync
            : DisableDohAutomaticallyAsync);
    }

    public string WindowsEncryptionLabel => EncryptionMode switch
    {
        DohEncryptionMode.Unencrypted => "Windows: без шифрования",
        DohEncryptionMode.EncryptedOnly => "Windows: зашифровано",
        DohEncryptionMode.EncryptedWithFallback => "Windows: зашифрованный вариант",
        _ => "Windows: неизвестный режим"
    };

    public bool IsUnencrypted
    {
        get => EncryptionMode == DohEncryptionMode.Unencrypted;
        set { if (value) EncryptionMode = DohEncryptionMode.Unencrypted; }
    }

    public bool IsEncryptedWithFallback
    {
        get => EncryptionMode == DohEncryptionMode.EncryptedWithFallback;
        set { if (value) EncryptionMode = DohEncryptionMode.EncryptedWithFallback; }
    }

    public bool IsEncryptedOnly
    {
        get => EncryptionMode == DohEncryptionMode.EncryptedOnly;
        set { if (value) EncryptionMode = DohEncryptionMode.EncryptedOnly; }
    }

    partial void OnEncryptionModeChanged(DohEncryptionMode value)
    {
        OnPropertyChanged(nameof(IsUnencrypted));
        OnPropertyChanged(nameof(IsEncryptedWithFallback));
        OnPropertyChanged(nameof(IsEncryptedOnly));
        OnPropertyChanged(nameof(WindowsEncryptionLabel));

        if (!_isInitialized)
            return;

        SaveDohPreferences();
        if (!IsDohEnabled
            || SelectedProvider is null || string.IsNullOrWhiteSpace(SelectedInterface))
        {
            return;
        }

        StartSerializedOperation(ApplyEncryptionModeAutomaticallyAsync);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
    private bool isBusy;

    [ObservableProperty] private string status = "DoH не настроен";

    [RelayCommand]
    private void RefreshInterfaces()
    {
        var selected = SelectedInterface;
        NetworkInterfaces.Clear();
        foreach (var name in GetActiveNetworkInterfaces())
        {
            NetworkInterfaces.Add(name);
        }

        SelectedInterface = NetworkInterfaces.FirstOrDefault(x => x == selected)
            ?? NetworkInterfaces.FirstOrDefault();
        RefreshActiveProvider();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsBusy = true;
        Status = "Проверяем DoH-провайдеров…";

        try
        {
            var tested = await _providerService.TestAllAsync(4, _scanCts.Token);
            var ranked = _selectionService.Rank(tested);
            Results.Clear();
            foreach (var result in ranked)
            {
                Results.Add(result);
            }

            if (AutomaticSelection)
            {
                SelectedProvider = _selectionService.SelectBest(ranked)?.Provider;
            }

            Status = SelectedProvider is null
                ? "Доступные DoH-провайдеры не найдены"
                : $"Рекомендуется: {SelectedProvider.Name}";
        }
        catch (OperationCanceledException)
        {
            Status = "Проверка отменена";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось проверить DoH-провайдеров");
            Status = "Ошибка проверки DoH-провайдеров";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApply() => !IsBusy && SelectedProvider is not null && !string.IsNullOrWhiteSpace(SelectedInterface);

    [RelayCommand(CanExecute = nameof(CanApply), IncludeCancelCommand = true)]
    private Task ApplyAsync(CancellationToken ct)
    {
        return RunSerializedOperationAsync(
            Interlocked.Increment(ref _operationGeneration),
            ApplySelectedConfigurationAsync,
            ct);
    }

    private async Task ApplyEncryptionModeAutomaticallyAsync(long generation, CancellationToken ct)
    {
        if (!IsCurrent(generation)) return;
        Status = "Применяем выбранный режим шифрования…";
        await ApplySelectedConfigurationAsync(generation, ct);
    }

    private async Task SwitchProviderAutomaticallyAsync(
        DohProvider previousProvider,
        DohProvider newProvider,
        long generation,
        CancellationToken ct)
    {
        var actuallyAppliedProvider = _appliedProvider ?? previousProvider;
        var actuallyAppliedMode = _appliedMode;
        IsBusy = true;
        try
        {
            Status = $"Переключаем DNS: {actuallyAppliedProvider.Name} → DHCP → {newProvider.Name}…";
            var result = await _providerSwitchService.SwitchAsync(
                SelectedInterface!, actuallyAppliedProvider, actuallyAppliedMode,
                newProvider, EncryptionMode, ct);
            if (!IsCurrent(generation)) return;
            Status = result.Message;
            if (result.IsSuccess)
            {
                _appliedProvider = newProvider;
                _appliedMode = EncryptionMode;
                SetActiveProvider(newProvider);
                SetDohToggleSilently(true);
                var settings = _settingsService.Load();
                settings.Doh.Enabled = true;
                settings.Doh.SelectedProviderId = newProvider.Id;
                settings.Doh.InterfaceName = SelectedInterface;
                settings.Doh.EncryptionMode = EncryptionMode;
                settings.Doh.AppliedProviderId = newProvider.Id;
                settings.Doh.AppliedEncryptionMode = EncryptionMode;
                settings.Doh.AppliedDnsAddresses = newProvider.DnsAddresses.ToList();
                _settingsService.Save(settings);
            }
            else if (result.SwitchOutcome == DohProviderSwitchOutcome.RestoredPrevious)
            {
                _isInitialized = false;
                SelectedProvider = actuallyAppliedProvider;
                _isInitialized = true;
                _appliedProvider = actuallyAppliedProvider;
                SetActiveProvider(actuallyAppliedProvider);
                _isInitialized = false;
                EncryptionMode = actuallyAppliedMode;
                _isInitialized = true;
                SetDohToggleSilently(true);
                var settings = _settingsService.Load();
                settings.Doh.Enabled = true;
                settings.Doh.SelectedProviderId = actuallyAppliedProvider.Id;
                settings.Doh.InterfaceName = SelectedInterface;
                settings.Doh.EncryptionMode = actuallyAppliedMode;
                settings.Doh.AppliedProviderId = actuallyAppliedProvider.Id;
                settings.Doh.AppliedEncryptionMode = actuallyAppliedMode;
                settings.Doh.AppliedDnsAddresses = actuallyAppliedProvider.DnsAddresses.ToList();
                _settingsService.Save(settings);
            }
            else if (result.SwitchOutcome == DohProviderSwitchOutcome.RestoreFailed)
            {
                SetUnsafeStateDisabled();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (IsCurrent(generation)) Status = "Переключение DNS-провайдера отменено";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось переключить DoH-провайдера");
            if (IsCurrent(generation))
            {
                Status = "Не удалось переключить DNS-провайдера; откат мог не завершиться: " + ex.Message;
                SetUnsafeStateDisabled();
            }
        }
        finally
        {
            if (IsCurrent(generation)) IsBusy = false;
        }
    }

    private async Task DisableDohAutomaticallyAsync(long generation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SelectedInterface))
        {
            Status = "Сетевой адаптер не выбран";
            return;
        }

        IsBusy = true;
        try
        {
            var safetyError = GetDisableSafetyError();
            if (safetyError is not null)
            {
                Status = safetyError;
                SetDohToggleSilently(true);
                return;
            }

            Status = "Отключаем DoH и возвращаем DNS от DHCP…";
            var result = await _configurationService.DisableAsync(SelectedInterface, ct);
            if (!IsCurrent(generation)) return;
            Status = result.Message;
            if (result.IsSuccess)
            {
                SetActiveProvider(null);
                SaveSettings(enabled: false, [], [], previousDnsWasDhcp: false);
            }
            else
            {
                SetDohToggleSilently(true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (IsCurrent(generation)) Status = "Отключение DoH отменено";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отключить DNS-over-HTTPS");
            if (IsCurrent(generation))
            {
                Status = "Не удалось вернуть DNS от DHCP; откат мог не завершиться: " + ex.Message;
                SetDohToggleSilently(true);
            }
        }
        finally
        {
            if (IsCurrent(generation)) IsBusy = false;
        }
    }

    private void SetDohToggleSilently(bool value)
    {
        _suppressDohToggle = true;
        IsDohEnabled = value;
        _suppressDohToggle = false;
    }

    private void SetUnsafeStateDisabled()
    {
        _appliedProvider = null;
        SetActiveProvider(null);
        SetDohToggleSilently(false);
        var settings = _settingsService.Load();
        settings.Doh.Enabled = false;
        settings.Doh.AppliedProviderId = null;
        settings.Doh.AppliedDnsAddresses = [];
        _settingsService.Save(settings);
    }

    private void StartSerializedOperation(Func<long, CancellationToken, Task> operation)
    {
        _modeApplyCts?.Cancel();
        _modeApplyCts?.Dispose();
        _modeApplyCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _operationGeneration);
        _operationTask = ObserveOperationAsync(
            RunSerializedOperationAsync(generation, operation, _modeApplyCts.Token), generation);
    }

    private async Task ObserveOperationAsync(Task operation, long generation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
            // Более новая операция штатно отменяет предыдущую.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Фоновая операция DoH завершилась с ошибкой");
            if (generation == Volatile.Read(ref _operationGeneration))
                Status = "Операция DoH завершилась с ошибкой: " + ex.Message;
        }
    }

    private async Task RunSerializedOperationAsync(
        long generation,
        Func<long, CancellationToken, Task> operation,
        CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            if (generation != Volatile.Read(ref _operationGeneration)) return;
            await operation(generation, ct);
            if (generation != Volatile.Read(ref _operationGeneration)) return;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ApplySelectedConfigurationAsync(long generation, CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var currentSettings = _settingsService.Load().Doh;
            var captured = await _dnsAdapterService.CaptureAsync(SelectedInterface!, ct);
            if (!IsCurrent(generation)) return;
            var preserveOriginal = currentSettings.Enabled
                && string.Equals(currentSettings.InterfaceName, SelectedInterface, StringComparison.OrdinalIgnoreCase)
                && currentSettings.PreviousDnsAddresses.Count > 0;
            var previous = preserveOriginal
                ? currentSettings.PreviousDnsAddresses
                : captured?.DnsAddresses ?? [];
            var previousWasDhcp = preserveOriginal
                ? currentSettings.PreviousDnsWasDhcp
                : captured?.IsDhcp == true;
            var result = await _configurationService.ApplyAsync(
                SelectedInterface!, SelectedProvider!, EncryptionMode, ct);
            if (!IsCurrent(generation)) return;
            Status = result.Message;
            if (result.IsSuccess)
            {
                _appliedProvider = SelectedProvider;
                _appliedMode = EncryptionMode;
                SetActiveProvider(SelectedProvider);
                SetDohToggleSilently(true);
                SaveSettings(
                    enabled: true,
                    previousDnsAddresses: previous,
                    appliedDnsAddresses: SelectedProvider!.DnsAddresses,
                    previousDnsWasDhcp: previousWasDhcp);
            }
            else
            {
                // Сервис транзакционно восстановил предыдущее состояние.
                // Не выключаем рабочий DoH только из-за неудачной смены режима.
                SetDohToggleSilently(currentSettings.Enabled);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (IsCurrent(generation)) Status = "Применение режима отменено";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось применить DNS-over-HTTPS");
            if (IsCurrent(generation)) Status = "Не удалось применить DoH; откат мог не завершиться: " + ex.Message;
        }
        finally
        {
            if (IsCurrent(generation)) IsBusy = false;
        }
    }

    private bool CanDisable() => !IsBusy && !string.IsNullOrWhiteSpace(SelectedInterface);

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private Task DisableAsync()
    {
        return RunSerializedOperationAsync(
            Interlocked.Increment(ref _operationGeneration),
            DisableCoreAsync,
            CancellationToken.None);
    }

    private async Task DisableCoreAsync(long generation, CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var saved = _settingsService.Load().Doh;
            if (!string.Equals(saved.InterfaceName, SelectedInterface, StringComparison.OrdinalIgnoreCase))
            {
                Status = "Снимок DNS относится к другому сетевому адаптеру; автоматический откат остановлен.";
                return;
            }

            var current = _getDnsAddresses(SelectedInterface!);
            if (saved.AppliedDnsAddresses.Count > 0
                && !current.SequenceEqual(saved.AppliedDnsAddresses, StringComparer.OrdinalIgnoreCase))
            {
                Status = "DNS изменён внешним приложением; автоматический откат остановлен.";
                return;
            }

            var result = await _configurationService.DisableAsync(SelectedInterface!, ct);
            if (!IsCurrent(generation)) return;
            Status = result.Message;
            if (result.IsSuccess)
            {
                SetActiveProvider(null);
                SaveSettings(enabled: false, [], [], previousDnsWasDhcp: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить DNS-конфигурацию");
            if (IsCurrent(generation)) Status = "Не удалось восстановить DNS: " + ex.Message;
        }
        finally
        {
            if (IsCurrent(generation)) IsBusy = false;
        }
    }

    private string? GetDisableSafetyError()
    {
        var saved = _settingsService.Load().Doh;
        if (!string.Equals(saved.InterfaceName, SelectedInterface, StringComparison.OrdinalIgnoreCase))
        {
            return "Снимок DNS относится к другому сетевому адаптеру; автоматический откат остановлен.";
        }

        var current = _getDnsAddresses(SelectedInterface!);
        return saved.AppliedDnsAddresses.Count > 0
            && !current.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(saved.AppliedDnsAddresses)
            ? "DNS изменён внешним приложением; автоматический откат остановлен."
            : null;
    }

    private void SaveDohPreferences()
    {
        var settings = _settingsService.Load();
        settings.Doh.AutomaticSelection = AutomaticSelection;
        settings.Doh.EncryptionMode = EncryptionMode;
        settings.Doh.SelectedProviderId = SelectedProvider?.Id;
        settings.Doh.InterfaceName = SelectedInterface;
        _settingsService.Save(settings);
    }

    private void SetActiveProvider(DohProvider? provider)
    {
        _activeProvider = provider;
        OnPropertyChanged(nameof(ActiveProviderText));
    }

    private void RefreshActiveProvider()
    {
        SetActiveProvider(DetectActiveProvider());
    }

    private DohProvider? DetectActiveProvider()
    {
        if (string.IsNullOrWhiteSpace(SelectedInterface))
            return null;

        IReadOnlyList<string> addresses;
        try
        {
            addresses = _getDnsAddresses(SelectedInterface);
        }
        catch
        {
            return null;
        }

        var current = addresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Providers.FirstOrDefault(provider =>
            provider.DnsAddresses.Any(current.Contains));
    }

    private void SaveSettings(
        bool enabled,
        IReadOnlyList<string> previousDnsAddresses,
        IReadOnlyList<string> appliedDnsAddresses,
        bool previousDnsWasDhcp)
    {
        var settings = _settingsService.Load();
        settings.Doh.Enabled = enabled;
        settings.Doh.AutomaticSelection = AutomaticSelection;
        settings.Doh.EncryptionMode = EncryptionMode;
        settings.Doh.SelectedProviderId = SelectedProvider?.Id;
        settings.Doh.AppliedProviderId = enabled ? _appliedProvider?.Id : null;
        settings.Doh.AppliedEncryptionMode = _appliedMode;
        settings.Doh.InterfaceName = SelectedInterface;
        settings.Doh.PreviousDnsWasDhcp = previousDnsWasDhcp;
        settings.Doh.PreviousDnsAddresses = previousDnsAddresses.ToList();
        settings.Doh.AppliedDnsAddresses = appliedDnsAddresses.ToList();
        _settingsService.Save(settings);
    }

    public Task WaitForPendingOperationAsync() => _operationTask;

    private bool IsCurrent(long generation) => generation == Volatile.Read(ref _operationGeneration);

    private static IReadOnlyList<string> GetDnsAddresses(string interfaceName)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(adapter => string.Equals(
                adapter.Name, interfaceName, StringComparison.OrdinalIgnoreCase))?
            .GetIPProperties().DnsAddresses
            .Select(address => address.ToString())
            .ToArray() ?? [];
    }

    private static IReadOnlyList<string> GetActiveNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                && adapter.GetIPProperties().GatewayAddresses.Count > 0)
            .Select(adapter => adapter.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}


