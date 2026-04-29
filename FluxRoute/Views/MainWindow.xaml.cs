using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using FluxRoute.Services;
using FluxRoute.ViewModels;
using Microsoft.Extensions.Logging;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingOperations = System.Windows.Data.BindingOperations;


namespace FluxRoute.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIconService _trayIcon;
    private readonly ILogger<MainWindow>? _logger;
    private bool _isClosingConfirmed;

    // Parameterless constructor is intentionally kept for the WPF designer
    // and as a safe fallback if the window is ever instantiated outside DI.
    public MainWindow()
        : this(new MainViewModel(), new TrayIconService(), null)
    {
    }

    public MainWindow(MainViewModel viewModel, TrayIconService trayIcon, ILogger<MainWindow>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(trayIcon);

        InitializeComponent();

        _vm = viewModel;
        _trayIcon = trayIcon;
        _logger = logger;

        DataContext = _vm;

        // Tray icon
        _trayIcon.SetVisible(true);
        _trayIcon.ShowRequested += OnTrayShowRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;

        _vm.ProfileSwitchNotification += OnProfileSwitched;

        // Auto-scroll service log
        _vm.ServiceLogs.CollectionChanged += ServiceLogs_CollectionChanged;

        // Animate sliding indicator on tab change
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Unified logs tab is declared in XAML so Visual Studio Designer and runtime show the same layout.
        _ = _vm.FilteredLogEntries;
        _vm.UnifiedLogEntries.CollectionChanged += UnifiedLogEntries_CollectionChanged;
        UpdatePageTitle();

        // Если запуск с --minimized (автозапуск), сворачиваем в трей
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
            _logger?.LogInformation("Main window started minimized because --minimized argument was provided.");
        }

        _logger?.LogInformation("Main window initialized.");
    }

    private void OnProfileSwitched(object? sender, string profileName)
    {
        _trayIcon.ShowBalloon("FluxRoute", $"Профиль переключён: {profileName}");
        _trayIcon.UpdateTooltip($"FluxRoute — {profileName}");
        _logger?.LogInformation("Active profile switched to {ProfileName}.", profileName);
    }

    private void OnTrayShowRequested(object? sender, EventArgs e)
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _isClosingConfirmed = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Сворачивание (—) → прячем в трей
        if (WindowState == WindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
            _trayIcon.ShowBalloon("FluxRoute", "Приложение свёрнуто в трей");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_isClosingConfirmed)
        {
            e.Cancel = true;

            if (CustomDialog.Show(
                    "Завершить работу FluxRoute?",
                    "Все активные службы (WinDivert, WinWS) будут остановлены, защита прекратит работу.",
                    "Завершить",
                    "Отмена",
                    isDanger: true))
            {
                _isClosingConfirmed = true;
                _logger?.LogInformation("User confirmed FluxRoute shutdown from main window.");

                if (!Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(Close);
                }
                else
                {
                    Close();
                }
            }

            return;
        }

        _logger?.LogInformation("Main window is closing. Starting application shutdown cleanup.");

        // Останавливаем winws.exe через ViewModel
        if (_vm.IsRunning)
            _vm.StopCommand.Execute(null);

        // Останавливаем TG WS Proxy
        _vm.StopTgProxyOnExit();

        // Очищаем ресурсы ViewModel (останавливаем оркестратор и таймеры)
        _vm.Cleanup();

        // Принудительно завершаем winws.exe и WinDivert
        ForceKillProcesses();

        _trayIcon.ShowRequested -= OnTrayShowRequested;
        _trayIcon.ExitRequested -= OnTrayExitRequested;
        _vm.ProfileSwitchNotification -= OnProfileSwitched;
        _vm.ServiceLogs.CollectionChanged -= ServiceLogs_CollectionChanged;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.UnifiedLogEntries.CollectionChanged -= UnifiedLogEntries_CollectionChanged;

        _trayIcon.Dispose();
        _logger?.LogInformation("Main window cleanup completed.");
    }

    private void ServiceLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ServiceLogScroll?.ScrollToEnd();
    }


    private void UnifiedLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_vm.LogsAutoScroll)
            return;

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                UnifiedLogsTextBox.CaretIndex = UnifiedLogsTextBox.Text.Length;
                UnifiedLogsTextBox.ScrollToEnd();
            }
            catch
            {
            }
        }));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            AnimateNavIndicator(_vm.SelectedTabIndex);
            UpdatePageTitle();
        }

        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
            AnimateSidebar(_vm.IsSidebarExpanded);
    }

    private void AnimateNavIndicator(int tabIndex)
    {
        // The About page is pinned to the bottom of the sidebar.
        // Logs use tab index 7, but visually they are placed right after Service
        // because About was removed from the main navigation list.
        if (tabIndex == 6)
        {
            SetNavIndicatorVisible(false);
            return;
        }

        SetNavIndicatorVisible(true);

        var visualIndex = tabIndex > 6 ? tabIndex - 1 : tabIndex;
        var animation = new DoubleAnimation
        {
            To = visualIndex * 36,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void AnimateSidebar(bool expanded)
    {
        var anim = new DoubleAnimation
        {
            To = expanded ? 165 : 48,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        SidebarBorder.BeginAnimation(WidthProperty, anim);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void ForceKillProcesses()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c taskkill /IM winws.exe /F >nul 2>&1 & net stop WinDivert >nul 2>&1",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch
        {
        }
    }

    private void SetNavIndicatorVisible(bool visible)
    {
        NavIndicatorBorder.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdatePageTitle()
    {
        if (_vm.SelectedTabIndex == 7)
        {
            PageTitleTextBlock.SetCurrentValue(TextBlock.TextProperty, "ЛОГИ");
            return;
        }

        WpfBindingOperations.SetBinding(PageTitleTextBlock, TextBlock.TextProperty, new WpfBinding("SelectedTabName"));
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }


}
