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

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private const int MaxUnifiedLogEntries = 5000;
    private bool _unifiedLogsInitialized;
    private ICollectionView? _filteredLogEntries;

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
            EnsureUnifiedLogsInitialized();
            return _filteredLogEntries!;
        }
    }

    [ObservableProperty]
    private string selectedLogCategory = "Все логи";

    partial void OnSelectedLogCategoryChanged(string value)
    {
        EnsureUnifiedLogsInitialized();
        _filteredLogEntries?.Refresh();
        RefreshUnifiedLogsText();
    }

    [ObservableProperty]
    private string logSearchText = string.Empty;

    partial void OnLogSearchTextChanged(string value)
    {
        EnsureUnifiedLogsInitialized();
        _filteredLogEntries?.Refresh();
        RefreshUnifiedLogsText();
    }

    [ObservableProperty]
    private bool logsAutoScroll = true;

    [ObservableProperty]
    private bool logsErrorsOnly;

    partial void OnLogsErrorsOnlyChanged(bool value)
    {
        EnsureUnifiedLogsInitialized();
        _filteredLogEntries?.Refresh();
        RefreshUnifiedLogsText();
    }

    [ObservableProperty]
    private string unifiedLogsText = string.Empty;

    private void EnsureUnifiedLogsInitialized()
    {
        if (_unifiedLogsInitialized)
            return;

        _unifiedLogsInitialized = true;
        _filteredLogEntries = CollectionViewSource.GetDefaultView(UnifiedLogEntries);
        _filteredLogEntries.Filter = FilterUnifiedLogEntry;

        AttachLogCollection(Logs, AppLogCategory.App);
        AttachLogCollection(OrchestratorLogs, AppLogCategory.Orchestrator);
        AttachLogCollection(RecentLogs, AppLogCategory.App);
        AttachLogCollection(UpdateLogs, AppLogCategory.Updater);
        AttachLogCollection(ServiceLogs, AppLogCategory.Service);
        AttachLogCollection(TgProxyLogs, AppLogCategory.TgProxy);

        if (UnifiedLogEntries.Count == 0)
            AppendUnifiedLog(AppLogCategory.App, "Вкладка логов инициализирована.");

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
        if (!_unifiedLogsInitialized || _filteredLogEntries is null)
            return;

        UnifiedLogsText = BuildVisibleLogText();
    }

    private string BuildVisibleLogText()
    {
        EnsureUnifiedLogsInitialized();
        return string.Join(Environment.NewLine, FilteredLogEntries.Cast<AppLogEntry>().Select(e => e.DisplayText));
    }

    [RelayCommand]
    private void ClearUnifiedLogs()
    {
        EnsureUnifiedLogsInitialized();
        UnifiedLogEntries.Clear();
        RefreshUnifiedLogsText();
        AppendUnifiedLog(AppLogCategory.App, "Логи очищены.");
    }

    [RelayCommand]
    private void CopyUnifiedLogs()
    {
        try
        {
            var text = BuildVisibleLogText();
            if (!string.IsNullOrWhiteSpace(text))
                System.Windows.Clipboard.SetText(text);

            AppendUnifiedLog(AppLogCategory.App, "Видимые логи скопированы в буфер обмена.");
        }
        catch (Exception ex)
        {
            AppendUnifiedLog(AppLogCategory.Error, $"Не удалось скопировать логи: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveUnifiedLogs()
    {
        try
        {
            var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
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
}
