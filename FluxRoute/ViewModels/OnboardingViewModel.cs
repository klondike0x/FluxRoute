using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel для окна онбординга при первом запуске.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    [ObservableProperty] private int selectedComponentIndex;

    /// <summary>
    /// Имя файла выбранной стратегии.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(IsStep3Visible))]
    private string? selectedStrategyFileName;

    public List<string> ComponentOptions { get; } = ["Zapret", "Zapret 2", "Без основного"];

    /// <summary>
    /// Список стратегий из engine/*.bat (ProfileItem как в MainViewModel).
    /// </summary>
    public ObservableCollection<ProfileItem> AvailableStrategies { get; } = new();

    /// <summary>
    /// Шаг 3 видим только после выбора стратегии.
    /// </summary>
    public bool IsStep3Visible => !string.IsNullOrEmpty(SelectedStrategyFileName);

    /// <summary>
    /// Кнопка «Настроить и продолжить» активна когда выбрана стратегия.
    /// </summary>
    public bool CanComplete => !string.IsNullOrEmpty(SelectedStrategyFileName);

    public OnboardingViewModel()
    {
        // Стратегии загружаются через LoadProfiles() после инициализации engine/
    }

    /// <summary>
    /// Загружает список стратегий из папки engine/.
    /// Вызывается из App.xaml.cs после того как engine скачан/проверен.
    /// </summary>
    public void LoadProfiles(string engineDir)
    {
        AvailableStrategies.Clear();

        if (!Directory.Exists(engineDir))
            return;

        try
        {
            foreach (var bat in Directory.GetFiles(engineDir, "*.bat"))
            {
                var fileName = System.IO.Path.GetFileName(bat);
                var displayName = System.IO.Path.GetFileNameWithoutExtension(bat)
                    .Replace("_", " ")
                    .Replace("-", " ");

                AvailableStrategies.Add(new ProfileItem
                {
                    FileName = fileName,
                    DisplayName = displayName,
                    FullPath = bat
                });
            }

            if (AvailableStrategies.Count > 0)
                SelectedStrategyFileName = AvailableStrategies[0].FileName;
        }
        catch
        {
            // engine/ не готов — список останется пустым
        }
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
    [RelayCommand(CanExecute = nameof(CanComplete))]
    private void Complete(Window? window)
    {
        if (window is null) return;
        window.DialogResult = true;
        window.Close();
    }
}
