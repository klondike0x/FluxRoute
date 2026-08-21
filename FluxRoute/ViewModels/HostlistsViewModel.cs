using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel вкладки Хостлисты.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class HostlistsViewModel : ObservableObject
{
    private readonly Func<string> _getEngineDir;
    private readonly Action<string> _addLog;
    private readonly Action<string, string>? _onSaved;

    public HostlistsViewModel(
        Func<string> getEngineDir,
        Action<string> addLog,
        Action<string, string>? onSaved = null)
    {
        _getEngineDir = getEngineDir;
        _addLog = addLog;
        _onSaved = onSaved;
    }

    public ObservableCollection<HostlistFileItem> Files { get; } = new();

    [ObservableProperty] private HostlistFileItem? selectedFile;
    [ObservableProperty] private string editorContent = string.Empty;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private bool hasChanges;
    [ObservableProperty] private string statusText = "Выберите файл для редактирования";

    private string _originalContent = string.Empty;

    partial void OnSelectedFileChanged(HostlistFileItem? value)
    {
        if (value is null) return;
        LoadFileContent(value);
    }

    partial void OnEditorContentChanged(string value)
    {
        if (!IsEditing) return;
        HasChanges = value != _originalContent;
    }

    /// <summary>
    /// Загружает список файлов хостлистов из engine/lists/.
    /// </summary>
    public void LoadHostlistFiles()
    {
        Files.Clear();
        var listsDir = Path.Combine(_getEngineDir(), "lists");
        if (!Directory.Exists(listsDir))
        {
            StatusText = "Папка engine/lists/ не найдена";
            return;
        }

        var knownFiles = new[] { "list-general.txt", "list-exclude.txt", "list-google.txt",
            "list-discord.txt", "list-youtube.txt", "list-general-user.txt", "list-exclude-user.txt" };
        var knownSet = new HashSet<string>(knownFiles, StringComparer.OrdinalIgnoreCase);

        // Сначала известные файлы
        foreach (var file in knownFiles)
        {
            var fullPath = Path.Combine(listsDir, file);
            Files.Add(new HostlistFileItem
            {
                FileName = file,
                FullPath = fullPath,
                Exists = File.Exists(fullPath)
            });
        }

        // Затем пользовательские (остальные .txt)
        foreach (var file in Directory.GetFiles(listsDir, "list-*.txt"))
        {
            var name = Path.GetFileName(file);
            if (knownSet.Contains(name)) continue;
            Files.Add(new HostlistFileItem
            {
                FileName = name,
                FullPath = file,
                Exists = true,
                IsCustom = true
            });
        }

        // ═══ v1.8.1: Системный файл hosts ═══
        var systemHostsPath = @"C:\Windows\System32\drivers\etc\hosts";
        Files.Add(new HostlistFileItem
        {
            FileName = "hosts (системный)",
            FullPath = systemHostsPath,
            Exists = File.Exists(systemHostsPath),
            IsSystemHosts = true
        });

        StatusText = $"Найдено файлов: {Files.Count}";
    }

    private void LoadFileContent(HostlistFileItem item)
    {
        try
        {
            if (item.Exists && File.Exists(item.FullPath))
            {
                EditorContent = File.ReadAllText(item.FullPath);
            }
            else
            {
                EditorContent = $"# {item.FileName} (файл отсутствует)\n";
            }
            _originalContent = EditorContent;
            IsEditing = true;
            HasChanges = false;
            StatusText = $"Редактирование: {item.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки: {ex.Message}";
            EditorContent = string.Empty;
        }
    }

    /// <summary>
    /// Сохраняет изменения в файл.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (SelectedFile is null) return;
        try
        {
            var dir = Path.GetDirectoryName(SelectedFile.FullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SelectedFile.FullPath, EditorContent);
            _onSaved?.Invoke(SelectedFile.FileName, EditorContent);
            _originalContent = EditorContent;
            HasChanges = false;
            SelectedFile.Exists = true;
            StatusText = $"Сохранено: {SelectedFile.FileName}";
            _addLog($"[Хостлисты] Сохранён файл: {SelectedFile.FileName}");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка сохранения: {ex.Message}";
        }
    }

    /// <summary>
    /// Отменяет изменения, возвращая исходное содержимое.
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedFile is null) return;
        EditorContent = _originalContent;
        HasChanges = false;
        StatusText = $"Изменения отменены: {SelectedFile.FileName}";
    }

    /// <summary>
    /// Восстанавливает файл из бэкапа (.bak).
    /// </summary>
    [RelayCommand]
    private void RestoreFromBackup()
    {
        if (SelectedFile is null) return;
        var backupPath = SelectedFile.FullPath + ".bak";
        try
        {
            if (!File.Exists(backupPath))
            {
                StatusText = $"Бэкап не найден: {Path.GetFileName(backupPath)}";
                return;
            }

            var backupContent = File.ReadAllText(backupPath);
            EditorContent = backupContent;
            _originalContent = backupContent;
            HasChanges = false;
            StatusText = $"Восстановлено из бэкапа: {SelectedFile.FileName}";
            _addLog($"[Хостлисты] Восстановлен из бэкапа: {SelectedFile.FileName}");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка восстановления: {ex.Message}";
        }
    }
}

/// <summary>
/// Элемент списка файлов хостлиста.
/// </summary>
public sealed class HostlistFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool IsCustom { get; set; }
    // ═══ v1.8.1: Флаг системного файла hosts ═══
    public bool IsSystemHosts { get; set; }

    public string DisplayText => $"{(Exists ? "◉" : "○")} {FileName}{(IsSystemHosts ? " 🔒" : "")}";
    public string DisplayTooltip => IsSystemHosts
        ? $"{FullPath}\nТребуются права администратора для сохранения"
        : FullPath;
}
