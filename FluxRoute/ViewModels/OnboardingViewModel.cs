using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel для окна онбординга при первом запуске.
/// v1.8.0: UI-Redesign
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    [ObservableProperty] private int selectedComponentIndex;
    [ObservableProperty] private string? selectedStrategyFileName;

    public List<string> ComponentOptions { get; } = ["Zapret", "Zapret 2", "Без основного"];
    public List<(string FileName, string DisplayName)> AvailableStrategies { get; }

    public OnboardingViewModel(List<(string FileName, string DisplayName)> strategies)
    {
        AvailableStrategies = strategies;
        if (strategies.Count > 0)
            SelectedStrategyFileName = strategies[0].FileName;
    }

    /// <summary>
    /// Выбранный компонент: 0=Zapret, 1=Zapret2, 2=None
    /// </summary>
    public string SelectedComponent =>
        SelectedComponentIndex switch
        {
            0 => "zapret",
            1 => "zapret2",
            _ => "none"
        };

    /// <summary>
    /// Завершить онбординг и сохранить выбор.
    /// </summary>
    [RelayCommand]
    private void Complete(Window? window)
    {
        window!.DialogResult = true;
        window.Close();
    }
}
