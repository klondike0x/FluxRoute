using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private LogsViewModel? _logPanel;

    /// <summary>
    /// Новая дочерняя ViewModel вкладки логов.
    /// Existing XAML пока остаётся совместимым через wrapper-свойства ниже.
    /// </summary>
    public LogsViewModel LogPanel
    {
        get
        {
            EnsureUnifiedLogsInitialized();
            return _logPanel!;
        }
    }

    public ObservableCollection<AppLogEntry> UnifiedLogEntries => LogPanel.UnifiedLogEntries;

    public IReadOnlyList<string> LogCategoryFilters => LogPanel.LogCategoryFilters;

    public ICollectionView FilteredLogEntries => LogPanel.FilteredLogEntries;

    [ObservableProperty]
    private string selectedLogCategory = "Все логи";

    partial void OnSelectedLogCategoryChanged(string value)
    {
        if (_logPanel is not null)
            _logPanel.SelectedLogCategory = value;

        OnPropertyChanged(nameof(UnifiedLogsText));
    }

    [ObservableProperty]
    private string logSearchText = string.Empty;

    partial void OnLogSearchTextChanged(string value)
    {
        if (_logPanel is not null)
            _logPanel.LogSearchText = value;

        OnPropertyChanged(nameof(UnifiedLogsText));
    }

    [ObservableProperty]
    private bool logsAutoScroll = true;

    [ObservableProperty]
    private bool logsErrorsOnly;

    partial void OnLogsErrorsOnlyChanged(bool value)
    {
        if (_logPanel is not null)
            _logPanel.LogsErrorsOnly = value;

        OnPropertyChanged(nameof(UnifiedLogsText));
    }

    public string UnifiedLogsText => LogPanel.UnifiedLogsText;

    private void EnsureUnifiedLogsInitialized()
    {
        if (_logPanel is not null)
            return;

        _logPanel = new LogsViewModel(
        [
            new LogSource(Logs, AppLogCategory.App),
            new LogSource(OrchestratorLogs, AppLogCategory.Orchestrator),
            new LogSource(RecentLogs, AppLogCategory.App),
            new LogSource(UpdateLogs, AppLogCategory.Updater),
            new LogSource(ServiceLogs, AppLogCategory.Service),
            new LogSource(TgProxyLogs, AppLogCategory.TgProxy)
        ])
        {
            SelectedLogCategory = SelectedLogCategory,
            LogSearchText = LogSearchText,
            LogsErrorsOnly = LogsErrorsOnly
        };

        _logPanel.PropertyChanged += OnLogPanelPropertyChanged;
        _logPanel.EnsureInitialized();

        OnPropertyChanged(nameof(LogPanel));
        OnPropertyChanged(nameof(UnifiedLogEntries));
        OnPropertyChanged(nameof(LogCategoryFilters));
        OnPropertyChanged(nameof(FilteredLogEntries));
        OnPropertyChanged(nameof(UnifiedLogsText));
    }

    private void OnLogPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogsViewModel.UnifiedLogsText))
            OnPropertyChanged(nameof(UnifiedLogsText));

        if (e.PropertyName == nameof(LogsViewModel.FilteredLogEntries))
            OnPropertyChanged(nameof(FilteredLogEntries));
    }

    [RelayCommand]
    private void ClearUnifiedLogs()
    {
        LogPanel.Clear();
    }

    [RelayCommand]
    private void CopyUnifiedLogs()
    {
        LogPanel.CopyVisibleLogsToClipboard();
    }

    [RelayCommand]
    private void SaveUnifiedLogs()
    {
        LogPanel.SaveVisibleLogsToFile(AppDomain.CurrentDomain.BaseDirectory);
    }
}
