using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace FluxRoute.ViewModels;

public enum HostlistUnsavedChangesDecision
{
    Save,
    Discard,
    Stay
}

/// <summary>
/// ViewModel вкладки Хостлисты.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class HostlistsViewModel : ObservableObject
{
    private readonly Func<string> _getEngineDir;
    private readonly Action<string> _addLog;
    private readonly Action<string, string>? _onSaved;
    private HostlistFileItem? _activeFile;

    /// <summary>
    /// UI callback для выбора действия при уходе с вкладки с несохранёнными изменениями.
    /// </summary>
    public Func<HostlistUnsavedChangesDecision>? UnsavedChangesPrompt { get; set; }

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

    partial void OnSelectedFileChanging(HostlistFileItem? value)
    {
        if (_isRestoringSelection
            || value is null
            || _activeFile is null
            || ReferenceEquals(value, _activeFile)
            || !HasChanges)
            return;

        switch (UnsavedChangesPrompt?.Invoke() ?? HostlistUnsavedChangesDecision.Stay)
        {
            case HostlistUnsavedChangesDecision.Save:
                Save();
                break;
            case HostlistUnsavedChangesDecision.Discard:
                CancelEdit();
                break;
            case HostlistUnsavedChangesDecision.Stay:
                _restoreSelection = true;
                break;
        }
    }

    partial void OnSelectedFileChanged(HostlistFileItem? value)
    {
        if (_restoreSelection)
        {
            _restoreSelection = false;
            _isRestoringSelection = true;
            SelectedFile = _activeFile;
            _isRestoringSelection = false;
            return;
        }

        if (value is null)
            return;

        _activeFile = value;
        LoadFileContent(value);
    }

    private bool _restoreSelection;
    private bool _isRestoringSelection;

    /// <summary>
    /// Проверяет, можно ли покинуть вкладку хостлистов.
    /// </summary>
    public bool TryLeave()
    {
        if (!HasChanges)
            return true;

        return (UnsavedChangesPrompt?.Invoke() ?? HostlistUnsavedChangesDecision.Stay) switch
        {
            HostlistUnsavedChangesDecision.Save => SaveAndConfirm(),
            HostlistUnsavedChangesDecision.Discard => DiscardAndConfirm(),
            _ => false
        };
    }

    private bool SaveAndConfirm()
    {
        Save();
        return !HasChanges;
    }

    private bool DiscardAndConfirm()
    {
        CancelEdit();
        return !HasChanges;
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
        var systemHostsPath = @"C:WindowsSystem32driversetchosts";
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
                EditorContent = $"# {item.FileName} (файл отсутствует)
";
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
        var file = SelectedFile ?? _activeFile;
        if (file is null) return;
        try
        {
            var dir = Path.GetDirectoryName(file.FullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var contentToSave = IsUserHostlist(file.FileName)
                ? NormalizeUserHostlistContent(EditorContent)
                : EditorContent;

            File.WriteAllText(file.FullPath, contentToSave);
            _onSaved?.Invoke(file.FileName, contentToSave);
            _originalContent = contentToSave;
            EditorContent = contentToSave;
            HasChanges = false;
            file.Exists = true;
            StatusText = $"Сохранено: {file.FileName}";
            _addLog($"[Хостлисты] Сохранён файл: {file.FileName}");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private static bool IsUserHostlist(string fileName) =>
        fileName.Equals("list-general-user.txt", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("list-exclude-user.txt", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUserHostlistContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        return string.Join(
            Environment.NewLine,
            content
                .Replace("
", "
", StringComparison.Ordinal)
                .Replace('', '
')
                .Split('
')
                .Select(NormalizeHostlistLine));
    }

    private static string NormalizeHostlistLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith(";", StringComparison.Ordinal))
            return line;

        var marker = trimmed.StartsWith("!", StringComparison.Ordinal) ? "!" : string.Empty;
        var value = marker.Length > 0 ? trimmed[1..].Trim() : trimmed;

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = value[8..];
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = value[7..];

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            value = value[4..];

        value = value.TrimEnd('/');
        return marker + value;
    }

    /// <summary>
    /// Отменяет изменения, возвращая исходное содержимое.
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        var file = SelectedFile ?? _activeFile;
        if (file is null) return;
        EditorContent = _originalContent;
        HasChanges = false;
        StatusText = $"Изменения отменены: {file.FileName}";
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
        ? $"{FullPath}
Требуются права администратора для сохранения"
        : FullPath;
}
