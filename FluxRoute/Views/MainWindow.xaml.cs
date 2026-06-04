using System.IO;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FluxRoute.AI.Services;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.ViewModels;
using Microsoft.Extensions.Logging;


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
        : this(CreateDesignTimeViewModel(), new TrayIconService(), null)
    {
    }

    /// <summary>
    /// ⚠️ WARNING: This method duplicates DI registration from App.xaml.cs.
    /// When changing service registration in App.xaml.cs, update this method too.
    /// </summary>
    private static MainViewModel CreateDesignTimeViewModel()
    {
        var settings = new SettingsService();
        var dir = Path.GetDirectoryName(settings.SettingsPath)!;
        var registry = new AiStrategyRegistry(Path.Combine(dir, "fluxroute-ai-strategies.json"));
        registry.Load();
        var history = new AiHistoryStore(Path.Combine(dir, "fluxroute-ai-history.jsonl"));
        var materializer = new BatMaterializer();
        var fingerprints = new NetworkFingerprintProvider();

        // ✅ Фабрика для design-time (чтобы не ломался конструктор MainViewModel)
        var httpClientFactory = FluxRoute.Core.Services.DefaultHttpClientFactory.Instance;

        return new MainViewModel(
            settings,
            new UpdaterService(httpClientFactory),
            new AppUpdaterService(httpClientFactory),
            new ConnectivityChecker(httpClientFactory),
            fingerprints,
            new NetworkChangeWatcher(fingerprints),
            registry,
            history,
            new BanditSelector(registry, new Random()),
            new StrategyEvolver(registry, history, materializer,
                () => Path.Combine(AppContext.BaseDirectory, "engine"),
                () => settings.Load().Ai),
            materializer,
            httpClientFactory);  // ✅ Новый параметр
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

        // Unified logs tab
        _vm.UnifiedLogEntries.CollectionChanged += UnifiedLogEntries_CollectionChanged;

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

        _trayIcon.Dispose();
        _logger?.LogInformation("Main window cleanup completed.");
    }

    private void ServiceLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ServiceLogScroll moved to ServicePage — autoscroll handled there
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            AnimateNavIndicator(_vm.SelectedTabIndex);
        }

        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
            AnimateSidebar(_vm.IsSidebarExpanded);

        if (e.PropertyName == nameof(MainViewModel.IsRunning))
        {
            if (_vm.IsRunning)
            {
                // Burst: кольца расходятся наружу при включении
                PlayWave(outward: true, strength: 0.65, duration: 1400);
                // После burst-волны — запускаем idle-пульс с задержкой
                var startDelay = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                startDelay.Tick += (_, _) => { startDelay.Stop(); StartIdlePulse(); };
                startDelay.Start();
            }
            else
            {
                // Сначала останавливаем idle
                StopIdlePulse();
                // Burst: кольца схлопываются внутрь при выключении
                PlayWave(outward: false, strength: 0.65, duration: 1400);
            }
        }
    }

    private void AnimateNavIndicator(int tabIndex)
        => SidebarControl.AnimateNavIndicator(tabIndex);

    private void AnimateSidebar(bool expanded)
    {
        // No-op in v1.5.0: sidebar is fixed-width icon-only; no expand/collapse animation.
    }

    // ── Wave pulse (делегируем в HomePage UserControl) ──

    private void PlayWave(bool outward, double strength, int duration)
        => HomeTab?.PlayWave(outward, strength, duration);

    private void StartIdlePulse()
        => HomeTab?.StartIdlePulse();

    private void StopIdlePulse()
        => HomeTab?.StopIdlePulse();

    private void TitleBar_MinimizeRequested(object sender, System.Windows.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void TitleBar_CloseRequested(object sender, System.Windows.RoutedEventArgs e)
        => Close();

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

    private void UnifiedLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (LogsTab is null || !_vm.LogsAutoScroll)
            return;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                LogsTab.ScrollToEnd();  // ← вызываем метод UserControl
            }
            catch { }
        }));
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}