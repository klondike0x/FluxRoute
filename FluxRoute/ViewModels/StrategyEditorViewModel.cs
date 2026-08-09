using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel для редактора .bat-файла стратегии.
/// v1.8.0: UI-Redesign
/// </summary>
public partial class StrategyEditorViewModel : ObservableObject
{
    public string FilePath { get; }
    public string OriginalFileName { get; }

    [ObservableProperty] private string editorContent = string.Empty;
    [ObservableProperty] private string windowTitle = "Редактор стратегии";
    [ObservableProperty] private bool hasChanges;
    [ObservableProperty] private string statusText = string.Empty;

    private readonly string _originalContent;
    private readonly Action<string>? _onSaved;

    public StrategyEditorViewModel(string filePath, Action<string>? onSaved = null)
    {
        FilePath = filePath;
        OriginalFileName = Path.GetFileName(filePath);
        WindowTitle = $"Редактор: {OriginalFileName}";

        try
        {
            _originalContent = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
            EditorContent = _originalContent;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки: {ex.Message}";
            _originalContent = string.Empty;
        }

        _onSaved = onSaved;
    }

    partial void OnEditorContentChanged(string value)
    {
        HasChanges = value != _originalContent;
    }

    /// <summary>
    /// Сохранить изменения в исходный файл.
    /// </summary>
    [RelayCommand]
    private void Save(Window? window)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Создаём бэкап перед сохранением
            if (File.Exists(FilePath))
            {
                var backupPath = FilePath + ".bak";
                File.Copy(FilePath, backupPath, overwrite: true);
            }

            File.WriteAllText(FilePath, EditorContent);
            StatusText = $"Сохранено: {OriginalFileName}";
            HasChanges = false;

            _onSaved?.Invoke(OriginalFileName);
            window?.Close();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка сохранения: {ex.Message}";
        }
    }

    /// <summary>
    /// Создать копию файла с суффиксом _copy.
    /// </summary>
    [RelayCommand]
    private void CreateCopy(Window? window)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            var nameNoExt = Path.GetFileNameWithoutExtension(OriginalFileName);
            var ext = Path.GetExtension(OriginalFileName);
            var copyName = $"{nameNoExt}_copy{ext}";
            var copyPath = Path.Combine(dir ?? string.Empty, copyName);

            // Уникальное имя если _copy уже существует
            var counter = 1;
            while (File.Exists(copyPath))
            {
                copyName = $"{nameNoExt}_copy{counter}{ext}";
                copyPath = Path.Combine(dir ?? string.Empty, copyName);
                counter++;
            }

            File.WriteAllText(copyPath, EditorContent);
            StatusText = $"Копия создана: {copyName}";
            HasChanges = false;

            _onSaved?.Invoke(copyName);
            window?.Close();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка копирования: {ex.Message}";
        }
    }

    /// <summary>
    /// Отменить изменения.
    /// </summary>
    [RelayCommand]
    private void Cancel(Window? window)
    {
        if (HasChanges)
        {
            var result = WpfMessageBox.Show(
                $"Отменить изменения в {OriginalFileName}?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        window?.Close();
    }
}
