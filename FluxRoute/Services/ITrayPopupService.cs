namespace FluxRoute.Services;

public interface ITrayPopupService
{
    event EventHandler? OpenApplicationRequested;
    event EventHandler? ExitApplicationRequested;

    void ShowAtCursor();
    void Close();
    void UpdateState(
        string? strategy,
        bool protectionIsRunning,
        bool orchestratorIsRunning,
        bool tgProxyIsRunning,
        bool gameFilterIsEnabled);
}
