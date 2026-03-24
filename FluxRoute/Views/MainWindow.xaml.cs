using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FluxRoute.Services;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIconService _trayIcon;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
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

        // Открываем настройки когда ViewModel просит
        _vm.OpenSettingsRequested += OnOpenSettingsRequested;
        _vm.OpenAboutRequested += OnOpenAboutRequested;
        _vm.ProfileSwitchNotification += OnProfileSwitched;

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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_isClosingConfirmed)
        {
            // Показываем оверлей подтверждения вместо закрытия (✕ или Alt+F4)
            e.Cancel = true;
            CloseOverlay.Visibility = Visibility.Visible;
            return;
        }

        // Останавливаем winws.exe через ViewModel
        if (_vm.IsRunning)
            _vm.StopCommand.Execute(null);

        // Принудительно завершаем winws.exe и WinDivert
        ForceKillProcesses();

        _trayIcon.Dispose();
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
        CloseOverlay.Visibility = Visibility.Visible;
    }

    private void CancelClose_Click(object sender, RoutedEventArgs e)
    {
        CloseOverlay.Visibility = Visibility.Collapsed;
    }

    private void ConfirmClose_Click(object sender, RoutedEventArgs e)
    {
        _isClosingConfirmed = true;
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

    private void OnOpenAboutRequested(object? sender, EventArgs e)
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow() { Owner = this };
        _aboutWindow.Show();
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_vm) { Owner = this };
        _settingsWindow.ShowDialog();
    }
}
