using System.IO;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;

using FluxRoute.Views;

namespace FluxRoute.ViewModels;

// Этот partial-файл теперь является тонкой обёрткой над ServiceViewModel.
// Вся бизнес-логика вынесена в ServiceViewModel.cs.
public partial class MainViewModel
{
    private void AddServiceLog(string message) => Service.ServiceLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

    private void RefreshServiceStatus() => Service.Refresh();

    private string ProtocolToFileValue(string protocol) => protocol switch
    {
        "TCP и UDP" => "all",
        "TCP" => "tcp",
        _ => "udp"
    };

    [RelayCommand]
    private void ToggleGameFilter() => Service.ToggleGameFilterCommand.Execute(null);

    [RelayCommand]
    private void CycleIpSetMode() => Service.CycleIpSetModeCommand.Execute(null);

    [RelayCommand]
    private async Task UpdateIpSetList() => await Service.UpdateIpSetListCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task UpdateHostsFile() => await Service.UpdateHostsFileCommand.ExecuteAsync(null);

    [RelayCommand]
    private void InstallZapretService() => Service.InstallZapretServiceCommand.Execute(null);

    [RelayCommand]
    private void ForceStopZapretService() => Service.ForceStopZapretServiceCommand.Execute(null);

    [RelayCommand]
    private void RefreshServiceInfo() => Service.RefreshServiceInfoCommand.Execute(null);
}