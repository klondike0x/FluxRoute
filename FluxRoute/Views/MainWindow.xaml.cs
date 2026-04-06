using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using FluxRoute.Services;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIconService _trayIcon;
    private bool _isClosingConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Tray icon
        _trayIcon = new TrayIconService();
        _trayIcon.SetVisible(true);
        _trayIcon.ShowRequested += OnTrayShowRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;

        _vm.ProfileSwitchNotification += OnProfileSwitched;

        // Auto-scroll service log
        _vm.ServiceLogs.CollectionChanged += ServiceLogs_CollectionChanged;

        // Animate sliding indicator on tab change
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Если запуск с --minimized (автозапуск), сворачиваем в трей
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized"))
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void OnProfileSwitched(object? sender, string profileName)
    {
        _trayIcon.ShowBalloon("FluxRoute", $"Профиль переключён: {profileName}");
        _trayIcon.UpdateTooltip($"FluxRoute — {profileName}");
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
                "Завершить", "Отмена", isDanger: true))
            {
                _isClosingConfirmed = true;
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

        // Останавливаем winws.exe через ViewModel
        if (_vm.IsRunning)
            _vm.StopCommand.Execute(null);

        // Очищаем ресурсы ViewModel (останавливаем оркестратор и таймеры)
        _vm.Cleanup();

        // Принудительно завершаем winws.exe и WinDivert
        ForceKillProcesses();

        _trayIcon.Dispose();
    }

    private void ServiceLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ServiceLogScroll?.ScrollToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
            AnimateNavIndicator(_vm.SelectedTabIndex);
        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
            AnimateSidebar(_vm.IsSidebarExpanded);
    }

    private void AnimateNavIndicator(int tabIndex)
    {
        var animation = new DoubleAnimation
        {
            To = tabIndex * 36,
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
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
            }
        }
        catch { }

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
        catch { }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
