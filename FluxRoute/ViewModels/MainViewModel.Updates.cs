using CommunityToolkit.Mvvm.Input;

namespace FluxRoute.ViewModels;

// Этот partial-файл — тонкая обёртка над UpdatesViewModel.
// Вся логика вынесена в UpdatesViewModel.cs.
public partial class MainViewModel
{
    [RelayCommand]
    private async Task CheckUpdates() => await Updates.CheckUpdatesCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task InstallUpdates() => await Updates.InstallUpdatesCommand.ExecuteAsync(null);

    private async Task AutoDownloadEngineAsync() => await Updates.AutoDownloadEngineAsync();

    private async Task CheckUpdatesOnStartupAsync() => await Updates.CheckOnStartupAsync();

    private void DisableNativeUpdateCheck()
    {
        try
        {
            var flagFile = System.IO.Path.Combine(EngineDir, "utils", "check_updates.enabled");
            if (System.IO.File.Exists(flagFile))
            {
                System.IO.File.Delete(flagFile);
                Logs.Add("Встроенная проверка обновлений zapret отключена.");
            }
        }
        catch (Exception ex) { Logs.Add($"Не удалось отключить проверку обновлений: {ex.Message}"); }
    }
}
