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
    /// Имя файла стратегии, выбранной автоматически для первой проверки.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(IsStep3Visible))]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteWithoutCheckCommand))]
    private string? selectedStrategyFileName;

    public List<string> ComponentOptions { get; } = ["Zapret"];

    /// <summary>
    /// Стратегии из engine/*.bat. Пользователь их не выбирает — для первой проверки используется первый доступный профиль.
    /// </summary>
    public ObservableCollection<ProfileItem> AvailableStrategies { get; } = new();

    /// <summary>
    /// Шаг 3 видим после загрузки профиля для первичной проверки.
    /// </summary>
    public bool IsStep3Visible => !string.IsNullOrEmpty(SelectedStrategyFileName);

    /// <summary>
    /// Варианты целей для первичной проверки.
    /// </summary>
    public List<string> ProbeTargetOptions { get; } = ["YouTube", "Discord", "YouTube + Discord"];

    [ObservableProperty]
    private int selectedProbeTargetIndex = 2;

    public bool ProbeYouTubeEnabled => SelectedProbeTargetIndex is 0 or 2;
    public bool ProbeDiscordEnabled => SelectedProbeTargetIndex is 1 or 2;

    partial void OnSelectedProbeTargetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ProbeYouTubeEnabled));
        OnPropertyChanged(nameof(ProbeDiscordEnabled));
    }

    /// <summary>
    /// Кнопка «Настроить и продолжить» активна, когда найден профиль для проверки.
    /// </summary>
    public bool CanComplete => !string.IsNullOrEmpty(SelectedStrategyFileName);

    /// <summary>
    /// Определяет, запускать ли первичное сканирование стратегий после onboarding.
    /// </summary>
    public bool ShouldRunInitialCheck { get; private set; } = true;

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
                if (string.Equals(fileName, "service.bat", StringComparison.OrdinalIgnoreCase))
                    continue;
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
        ShouldRunInitialCheck = true;
        if (window is null) return;
        window.DialogResult = true;
        window.Close();
    }

    /// <summary>
    /// Завершить onboarding без запуска первичной проверки стратегий.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanComplete))]
    private void CompleteWithoutCheck(Window? window)
    {
        ShouldRunInitialCheck = false;
        if (window is null) return;
        window.DialogResult = true;
        window.Close();
    }
}
