using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel для страницы управления модами в стиле Zapret-Hub.
/// </summary>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly IModManager _modManager;
    private readonly ILogger<ModsViewModel>? _logger;

    /// <summary>
    /// Вызывается после изменения статуса мода (вкл/выкл) для обновления главного списка профилей.
    /// Устанавливается из App.xaml.cs после создания MainViewModel.
    /// </summary>
    public Action? OnModStatusChanged { get; set; }

    /// <summary>Список модов.</summary>
    [ObservableProperty]
    private ObservableCollection<ModInfo> _mods = new();

    /// <summary>Есть ли хотя бы один мод.</summary>
    [ObservableProperty]
    private bool _hasMods;

    /// <summary>Флаг загрузки.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Сообщение о статусе.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ModsViewModel(IModManager modManager, ILogger<ModsViewModel>? logger = null)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger;
    }

    /// <summary>Загружает список модов.</summary>
    [RelayCommand]
    private async Task LoadModsAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Сканирование модов...";
            _logger?.LogInformation("Загрузка списка модов");

            var mods = await _modManager.ScanModsAsync().ConfigureAwait(true);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Mods.Clear();
                foreach (var mod in mods)
                    Mods.Add(mod);
                HasMods = Mods.Count > 0;
            });

            StatusMessage = HasMods ? $"Найдено модов: {Mods.Count}" : string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка загрузки списка модов");
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Включает/выключает мод по нажатию на toggle.</summary>
    [RelayCommand]
    private async Task ToggleModAsync(ModInfo? mod)
    {
        if (mod == null) return;

        try
        {
            if (mod.Status == ModStatus.Active)
            {
                await _modManager.DeactivateModAsync(mod.FolderName).ConfigureAwait(true);
                UpdateModInCollection(mod, ModStatus.Inactive);
                StatusMessage = $"{mod.Name} выключен";
                OnModStatusChanged?.Invoke();
            }
            else
            {
                await _modManager.ActivateModAsync(mod.FolderName).ConfigureAwait(true);
                UpdateModInCollection(mod, ModStatus.Active);
                StatusMessage = $"{mod.Name} включён";
                OnModStatusChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка переключения мода {Folder}", mod.FolderName);
            UpdateModInCollection(mod, ModStatus.Error, ex.Message);
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }

    /// <summary>Создать новый мод: диалог ввода имени → папка + manifest + скрипты.</summary>
    [RelayCommand]
    private async Task CreateModAsync()
    {
        var name = FluxRoute.Views.ModNameDialog.ShowCreateDialog();
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            StatusMessage = $"Создание мода «{name}»...";
            await _modManager.CreateModAsync(name).ConfigureAwait(true);

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadModsAsync();
            });
            OnModStatusChanged?.Invoke();
            StatusMessage = $"Мод «{name}» создан";
            _logger?.LogInformation("Создан мод: {Name}", name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка создания мода {Name}", name);
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }

    /// <summary>Импортировать мод: диалог выбора источника (папка/ZIP/файлы/GitHub).</summary>
    [RelayCommand]
    private async Task ImportModAsync()
    {
        var result = FluxRoute.Views.ModImportDialog.ShowImportDialog();
        if (result == null)
            return;

        try
        {
            StatusMessage = "Импорт мода...";
            List<ModInfo> imported;

            switch (result.Source)
            {
                case FluxRoute.Views.ModImportSource.Folder:
                    imported = new List<ModInfo>
                    {
                        await _modManager.ImportFromFolderAsync(result.Path).ConfigureAwait(true)
                    };
                    break;
                case FluxRoute.Views.ModImportSource.Zip:
                    imported = await _modManager.ImportFromZipAsync(result.Path).ConfigureAwait(true);
                    break;
                case FluxRoute.Views.ModImportSource.Files:
                    var files = result.Path.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    imported = new List<ModInfo>
                    {
                        await _modManager.ImportFromFilesAsync(files).ConfigureAwait(true)
                    };
                    break;
                case FluxRoute.Views.ModImportSource.Github:
                    imported = await _modManager.ImportFromGithubAsync(result.Path).ConfigureAwait(true);
                    break;
                default:
                    return;
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadModsAsync();
            });

            OnModStatusChanged?.Invoke();
            StatusMessage = imported.Count switch
            {
                0 => "Не удалось импортировать моды",
                1 => $"Мод «{imported[0].Name}» импортирован",
                _ => $"Импортировано модов: {imported.Count}"
            };
            _logger?.LogInformation("Импортировано модов: {Count} ({Names})",
                imported.Count, string.Join(", ", imported.Select(m => m.Name)));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка импорта мода");
            StatusMessage = $"Ошибка импорта: {ex.Message}";
        }
    }

    /// <summary>Редактировать мод.</summary>
    [RelayCommand]
    private void EditMod(ModInfo? mod)
    {
        if (mod == null) return;
        // TODO: диалог редактирования
        StatusMessage = $"Редактирование {mod.Name}";
    }

    /// <summary>Экспортировать мод.</summary>
    [RelayCommand]
    private void ExportMod(ModInfo? mod)
    {
        if (mod == null) return;
        // TODO: экспорт в ZIP
        StatusMessage = $"Экспорт {mod.Name}";
    }

    /// <summary>Удалить мод.</summary>
    [RelayCommand]
    private async Task DeleteModAsync(ModInfo? mod)
    {
        if (mod == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Удалить мод «{mod.Name}»?\nПапка mods/{mod.FolderName} будет удалена безвозвратно.",
            "Удаление мода",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var modPath = System.IO.Path.Combine(AppContext.BaseDirectory, "mods", mod.FolderName);
            if (System.IO.Directory.Exists(modPath))
                System.IO.Directory.Delete(modPath, recursive: true);

            await Application.Current.Dispatcher.InvokeAsync(() => Mods.Remove(mod));
            HasMods = Mods.Count > 0;
            OnModStatusChanged?.Invoke();
            StatusMessage = $"{mod.Name} удалён";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка удаления мода {Folder}", mod.FolderName);
            StatusMessage = $"Ошибка удаления: {ex.Message}";
        }
    }

    /// <summary>Обновить список.</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadModsAsync();

    /// <summary>
    /// Синхронизирует моды из GitHub-репозитория: скачивает, находит все моды и
    /// устанавливает недостающие (уже установленные пропускаются).
    /// </summary>
    [RelayCommand]
    private async Task SyncFromGithubAsync()
    {
        var url = FluxRoute.Views.ModGithubUrlDialog.ShowGithubUrlDialog();
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            StatusMessage = "Синхронизация модов из GitHub...";
            _logger?.LogInformation("Синхронизация модов из GitHub: {Url}", url);

            var result = await _modManager.SyncFromGithubAsync(url).ConfigureAwait(true);

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadModsAsync();
            });

            OnModStatusChanged?.Invoke();
            StatusMessage = result.Added switch
            {
                0 when result.Skipped > 0 => $"Все моды уже установлены ({result.Skipped} пропущено)",
                0 => "Новых модов не найдено",
                1 => $"Установлен мод: {result.AddedNames[0]}",
                _ => $"Установлено модов: {result.Added} (пропущено: {result.Skipped})"
            };
            _logger?.LogInformation("Синхронизация завершена: добавлено {Added}, пропущено {Skipped}",
                result.Added, result.Skipped);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка синхронизации модов из GitHub");
            StatusMessage = $"Ошибка синхронизации: {ex.Message}";
        }
    }

    private void UpdateModInCollection(ModInfo mod, ModStatus status, string? error = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            mod.Status = status;
            mod.ErrorMessage = error;
            var idx = Mods.IndexOf(mod);
            if (idx >= 0)
            {
                Mods.RemoveAt(idx);
                Mods.Insert(idx, mod);
            }
        });
    }
}
