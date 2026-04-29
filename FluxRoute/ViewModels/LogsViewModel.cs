using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace FluxRoute.ViewModels;

/// <summary>
/// ViewModel единой вкладки логов.
///
/// Класс намеренно изолирует агрегацию, фильтрацию, поиск и экспорт логов от MainViewModel.
/// MainViewModel пока оставляет совместимые wrapper-свойства, чтобы не менять XAML и снизить риск регрессий.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject
{
    private const int MaxUnifiedLogEntries = 5000;
    private readonly IReadOnlyList<LogSource> _sources;
    private bool _initialized;
    private ICollectionView? _filteredLogEntries;

    public LogsViewModel(IReadOnlyList<LogSource> sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public ObservableCollection<AppLogEntry> UnifiedLogEntries { get; } = new();

    public IReadOnlyList<string> LogCategoryFilters { get; } =
    [
        "Все логи",
        "Приложение",
        "Оркестратор",
        "Сканирование профилей",
        "Запуск профиля / winws.exe",
        "TG WS Proxy",
        "Обновление engine",
        "Сервис",
        "Ошибки"
    ];

    public ICollectionView FilteredLogEntries
    {
        get
        {
            EnsureInitialized();
            return _filteredLogEntries!;
        }
    }

    [ObservableProperty]
    private string selectedLogCategory = "Все логи";

    partial void OnSelectedLogCategoryChanged(string value)
    {
        RefreshFilter();
    }

    [ObservableProperty]
    private string logSearchText = string.Empty;

    partial void OnLogSearchTextChanged(string value)
    {
        RefreshFilter();
    }

    [ObservableProperty]
    private bool logsErrorsOnly;

    partial void OnLogsErrorsOnlyChanged(bool value)
    {
        RefreshFilter();
    }

    [ObservableProperty]
    private string unifiedLogsText = string.Empty;

    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        _filteredLogEntries = CollectionViewSource.GetDefaultView(UnifiedLogEntries);
        _filteredLogEntries.Filter = FilterUnifiedLogEntry;

        foreach (var source in _sources)
            AttachLogCollection(source.Items, source.DefaultCategory);

        if (UnifiedLogEntries.Count == 0)
            AppendUnifiedLog(AppLogCategory.App, "Вкладка логов инициализирована.");

        RefreshUnifiedLogsText();
    }

    public string BuildVisibleLogText()
    {
        EnsureInitialized();
        return string.Join(Environment.NewLine, FilteredLogEntries.Cast<AppLogEntry>().Select(e => e.DisplayText));
    }

    public void Clear()
    {
        EnsureInitialized();
        UnifiedLogEntries.Clear();
        RefreshUnifiedLogsText();
        AppendUnifiedLog(AppLogCategory.App, "Логи очищены.");
    }

    public void CopyVisibleLogsToClipboard()
    {
        try
        {
            var text = BuildVisibleLogText();
            if (!string.IsNullOrWhiteSpace(text))
                Clipboard.SetText(text);

            AppendUnifiedLog(AppLogCategory.App, "Видимые логи скопированы в буфер обмена.");
        }
        catch (Exception ex)
        {
            AppendUnifiedLog(AppLogCategory.Error, $"Не удалось скопировать логи: {ex.Message}");
        }
    }

    public void SaveVisibleLogsToFile(string baseDirectory)
    {
        try
        {
            var logsDir = Path.Combine(baseDirectory, "logs");
            Directory.CreateDirectory(logsDir);

            var filePath = Path.Combine(logsDir, $"fluxroute-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(filePath, BuildVisibleLogText(), Encoding.UTF8);

            AppendUnifiedLog(AppLogCategory.App, $"Логи сохранены: {filePath}");
        }
        catch (Exception ex)
        {
            AppendUnifiedLog(AppLogCategory.Error, $"Не удалось сохранить логи: {ex.Message}");
        }
    }

    private void RefreshFilter()
    {
        EnsureInitialized();
        _filteredLogEntries?.Refresh();
        RefreshUnifiedLogsText();
    }

    private void AttachLogCollection(ObservableCollection<string> source, AppLogCategory category)
    {
        foreach (var item in source)
            AppendUnifiedLog(DetectCategory(category, item), item);

        source.CollectionChanged += (_, e) => OnSourceLogCollectionChanged(category, e);
    }

    private void OnSourceLogCollectionChanged(AppLogCategory defaultCategory, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
            return;

        foreach (var item in e.NewItems)
        {
            var message = item?.ToString();
            if (string.IsNullOrWhiteSpace(message))
                continue;

            AppendUnifiedLog(DetectCategory(defaultCategory, message), message);
        }
    }

    private void AppendUnifiedLog(AppLogCategory category, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(new Action(() => AppendUnifiedLog(category, message)));
            return;
        }

        var level = DetectLevel(message);
        if (level == AppLogLevel.Error)
            category = AppLogCategory.Error;

        UnifiedLogEntries.Add(new AppLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Category = category,
            Level = level,
            Message = NormalizeLogMessage(message)
        });

        while (UnifiedLogEntries.Count > MaxUnifiedLogEntries)
            UnifiedLogEntries.RemoveAt(0);

        RefreshUnifiedLogsText();
    }

    private static string NormalizeLogMessage(string message)
    {
        return message.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static AppLogCategory DetectCategory(AppLogCategory fallback, string message)
    {
        if (message.Contains("[Оркестратор]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("оркестратор", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.Orchestrator;

        if (message.Contains("скан", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("curl", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("score", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("рейтинг", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.ProfileScan;

        if (message.Contains("winws", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("профил", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("PID", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.Process;

        if (message.Contains("TG WS", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("tg_ws_proxy", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("прокси", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.TgProxy;

        if (message.Contains("обнов", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Flowseal", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("engine", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.Updater;

        if (message.Contains("сервис", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("WinDivert", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Game Filter", StringComparison.OrdinalIgnoreCase))
            return AppLogCategory.Service;

        return fallback;
    }

    private static AppLogLevel DetectLevel(string message)
    {
        if (message.Contains('❌') ||
            message.Contains("ошибка", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return AppLogLevel.Error;

        if (message.Contains('⚠') ||
            message.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("таймаут", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("предупреж", StringComparison.OrdinalIgnoreCase))
            return AppLogLevel.Warning;

        return AppLogLevel.Info;
    }

    private bool FilterUnifiedLogEntry(object obj)
    {
        if (obj is not AppLogEntry entry)
            return false;

        if (LogsErrorsOnly && entry.Level != AppLogLevel.Error)
            return false;

        if (!string.IsNullOrWhiteSpace(SelectedLogCategory) &&
            !string.Equals(SelectedLogCategory, "Все логи", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.CategoryText, SelectedLogCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(LogSearchText))
        {
            var query = LogSearchText.Trim();
            if (!entry.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void RefreshUnifiedLogsText()
    {
        if (!_initialized || _filteredLogEntries is null)
            return;

        UnifiedLogsText = BuildVisibleLogText();
    }

    [RelayCommand]
    private void ClearUnifiedLogs() => Clear();

    [RelayCommand]
    private void CopyUnifiedLogs() => CopyVisibleLogsToClipboard();

    [RelayCommand]
    private void SaveUnifiedLogs() => SaveVisibleLogsToFile(AppDomain.CurrentDomain.BaseDirectory);
}

public sealed record LogSource(ObservableCollection<string> Items, AppLogCategory DefaultCategory);
