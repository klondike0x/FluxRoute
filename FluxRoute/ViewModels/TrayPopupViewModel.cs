using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FluxRoute.ViewModels;

public sealed partial class TrayPopupViewModel : ObservableObject
{
    private readonly Action _openApplication;
    private readonly Action _exitApplication;

    [ObservableProperty]
    private string version = "v1.6.0";

    [ObservableProperty]
    private string strategyName = "—";

    [ObservableProperty]
    private bool protectionRunning;

    [ObservableProperty]
    private bool orchestratorRunning;

    [ObservableProperty]
    private bool tgProxyRunning;

    [ObservableProperty]
    private bool gameFilterEnabled;

    public TrayPopupViewModel(Action openApplication, Action exitApplication)
    {
        _openApplication = openApplication ?? throw new ArgumentNullException(nameof(openApplication));
        _exitApplication = exitApplication ?? throw new ArgumentNullException(nameof(exitApplication));
    }

    public string ProtectionStatus => ProtectionRunning ? "Защита включена" : "Защита выключена";
    public string OrchestratorStatus => OrchestratorRunning ? "Включён" : "Выключен";
    public string TgProxyStatus => TgProxyRunning ? "Работает" : "Остановлен";
    public string GameFilterStatus => GameFilterEnabled ? "Включён" : "Выключен";

    partial void OnProtectionRunningChanged(bool value) => OnPropertyChanged(nameof(ProtectionStatus));
    partial void OnOrchestratorRunningChanged(bool value) => OnPropertyChanged(nameof(OrchestratorStatus));
    partial void OnTgProxyRunningChanged(bool value) => OnPropertyChanged(nameof(TgProxyStatus));
    partial void OnGameFilterEnabledChanged(bool value) => OnPropertyChanged(nameof(GameFilterStatus));

    public void UpdateState(
        string? strategy,
        bool protectionIsRunning,
        bool orchestratorIsRunning,
        bool tgProxyIsRunning,
        bool gameFilterIsEnabled)
    {
        StrategyName = string.IsNullOrWhiteSpace(strategy) ? "—" : strategy;
        ProtectionRunning = protectionIsRunning;
        OrchestratorRunning = orchestratorIsRunning;
        TgProxyRunning = tgProxyIsRunning;
        GameFilterEnabled = gameFilterIsEnabled;
    }

    [RelayCommand]
    private void OpenApplication() => _openApplication();

    [RelayCommand]
    private void ExitApplication() => _exitApplication();
}
