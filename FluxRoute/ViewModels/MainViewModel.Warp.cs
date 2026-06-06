using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Services.Warp;
using System.Net.Http;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string warpConfigResult = "";

    [RelayCommand]
    private async Task GenerateWarpConfig()
    {
        WarpConfigResult = "Генерация...";
        try
        {
            var service = new WarpService(new HttpClient());
            var config = await service.RegisterAsync();
            WarpConfigResult = service.GenerateWireGuardConfig(config);
            Logs.Add("[Warp] Конфигурация успешно сгенерирована.");
        }
        catch (Exception ex)
        {
            WarpConfigResult = $"Ошибка: {ex.Message}";
            Logs.Add($"[Warp] Ошибка: {ex.Message}");
        }
    }
}
