using System.IO;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

    // Таймер для троттлинга (защита от дёрганий таблетки при частом ресайзе окна)
    private readonly System.Windows.Threading.DispatcherTimer _navIndicatorResizeTimer;

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
            httpClientFactory,
            taskScheduler: new TaskSchedulerService(),
            trayIcon: null);
    }

    public MainWindow(MainViewModel viewModel, TrayIconService trayIcon, ILogger<MainWindow>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(trayIcon);

        InitializeComponent();

        // Ограничиваем maximized-окно рабочей областью монитора,
        // чтобы оно не перекрывало панель задач и не выходило за экран.
        SourceInitialized += OnSourceInitialized;

        _vm = viewModel;
        _trayIcon = trayIcon;
        _logger = logger;

        DataContext = _vm;

        // Инициализируем подсветку активного пункта Sidebar после построения визуального дерева.
        Loaded += (_, _) => SidebarControl?.AnimateNavIndicator(_vm.SelectedTabIndex, animate: false);

        // Tray icon
        _trayIcon.SetVisible(true);
        _trayIcon.ShowRequested += OnTrayShowRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;

        _vm.ProfileSwitchNotification += OnProfileSwitched;

        // Auto-scroll service log
        _vm.ServiceLogs.CollectionChanged += ServiceLogs_CollectionChanged;

        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Unified logs tab
        _vm.UnifiedLogEntries.CollectionChanged += UnifiedLogEntries_CollectionChanged;

        // Инициализация таймера для троттлинга (задержка 0 мс)
        _navIndicatorResizeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(0)
        };
        _navIndicatorResizeTimer.Tick += OnNavIndicatorResizeTimerTick;

        // Обновление таблетки при ресайзе
        SizeChanged += OnWindowSizeChanged;

        // Если запуск с --minimized (автозапуск через планировщик), сворачиваем в трей.
        // Также сворачиваем если MinimizeToTray == true (пользовательская настройка).
        // НЕЛЬЗЯ вызывать Hide() в конструкторе — WPF ещё не отрисовал окно,
        // и планировщик задач покажет «битое» окно. Откладываем до Loaded.
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) || _vm.MinimizeToTray)
        {
            Loaded += OnLoadedForMinimizedStart;
            _logger?.LogInformation("Minimized start scheduled — window will hide after Loaded (--minimized={Arg}, MinimizeToTray={Setting}).",
                args.Contains("--minimized", StringComparer.OrdinalIgnoreCase), _vm.MinimizeToTray);
        }

        _logger?.LogInformation("Main window initialized.");
    }

    private void OnProfileSwitched(object? sender, string profileName)
    {
        _trayIcon.ShowBalloon("FluxRoute", $"Стратегия переключена: {profileName}");
        _trayIcon.UpdateTooltip($"FluxRoute — {profileName}");
        _logger?.LogInformation("Active profile switched to {ProfileName}.", profileName);
    }

    /// <summary>
    /// Отложенное сворачивание в трей для автозапуска (--minimized).
    /// Вызывается после полной отрисовки окна, чтобы избежать
    /// «битого» рендеринга при запуске через Планировщик задач.
    /// </summary>
    private void OnLoadedForMinimizedStart(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForMinimizedStart;
        Hide();
        ShowInTaskbar = false;
        _logger?.LogInformation("Main window hidden to tray after Loaded (--minimized).");
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
        // Показываем модальное подтверждение перед закрытием
        if (CustomDialog.Show(
                "Завершить работу FluxRoute?",
                "Все активные службы (WinDivert, WinWS) будут остановлены, защита прекратит работу.",
                "Завершить",
                "Отмена",
                isDanger: true))
        {
            _isClosingConfirmed = true;
            Close();
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // В maximized-режиме внешние углы должны быть прямыми.
        if (RootBorder is not null)
            RootBorder.CornerRadius = WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(16);

        // Сворачивание (—): стандартное поведение Windows — окно остаётся на панели задач.
        // Трей — только через кнопку закрытия (CloseToTray).
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // ═══ v1.6.0: Feature #21 — Крестик сворачивает в трей ═══
        if (_vm.CloseToTray && !_isClosingConfirmed)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            _trayIcon.ShowBalloon("FluxRoute", "Приложение свёрнуто в трей");
            return;
        }
        // ═══════════════════════════════════════════════════════════

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
            SidebarControl.AnimateNavIndicator(_vm.SelectedTabIndex);
        }

        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
            AnimateSidebar(_vm.IsSidebarExpanded);

        if (e.PropertyName == nameof(MainViewModel.IsRunning))
        {
            _trayIcon.UpdateIcon(_vm.IsRunning);
            UpdateTrayMenu();

            if (_vm.IsRunning)
            {
                // Burst: кольца расходятся наружу при включении
                PlayWave(outward: true, strength: 0.65, duration: 1400);
                // AFTER burst-волны — запускаем idle-пульс с задержкой
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

        // ═══ v1.6.0: Обновление меню трея при изменении статусов ═══
        if (e.PropertyName is nameof(MainViewModel.SelectedProfile)
            or nameof(MainViewModel.OrchestratorEnabled)
            or nameof(MainViewModel.TgProxyRunning)
            or nameof(MainViewModel.GameFilterEnabled))
        {
            UpdateTrayMenu();
        }
    }

    /// <summary>
    /// Синхронизирует контекстное меню трея с текущим состоянием ViewModel.
    /// </summary>
    private void UpdateTrayMenu()
    {
        _trayIcon.UpdateMenu(
            strategy: _vm.SelectedProfile?.DisplayName,
            orchestratorRunning: _vm.OrchestratorEnabled,
            tgProxyRunning: _vm.TgProxyRunning,
            gameFilterEnabled: _vm.GameFilterEnabled);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Сбрасываем таймер при каждом изменении размера.
        // Анимация сработает только когда ресайз прекратится на 80 мс.
        _navIndicatorResizeTimer.Stop();
        _navIndicatorResizeTimer.Start();
    }

    private void OnNavIndicatorResizeTimerTick(object? sender, EventArgs e)
        {
            // Останавливаем таймер, чтобы он не срабатывал повторно
            _navIndicatorResizeTimer.Stop();

            var tab = _vm.SelectedTabIndex;

            // Откладываем вызов до момента, когда WPF полностью завершит
            // пересчёт layout (DispatcherPriority.Render).
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                SidebarControl?.AnimateNavIndicator(tab);
            }));

            // Авто-разворот Sidebar при ширине ≥ 1200
            UpdateSidebarExpansion();
        }

        private void UpdateSidebarExpansion()
        {
            var shouldExpand = ActualWidth >= 1200;
            if (_vm.IsSidebarExpanded != shouldExpand)
            {
                _vm.IsSidebarExpanded = shouldExpand;
                AnimateSidebar(shouldExpand);
            }
        }

        private void AnimateSidebar(bool expanded)
        {
            if (SidebarControl is null) return;
            SidebarControl.IsExpanded = expanded;
            AnimateSidebarWidth(SidebarColumn.Width.Value, expanded ? 241.0 : 66.0);
        }

        private void AnimateSidebarWidth(double from, double to)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            var elapsed = 0.0;
            const double duration = 200.0;
            timer.Tick += (s, e) =>
            {
                elapsed += 16;
                if (elapsed >= duration)
                {
                    SidebarColumn.Width = new GridLength(to);
                    timer.Stop();
                    return;
                }
                var t = elapsed / duration;
                // EaseOut quadratic: 1 - (1-t)^2
                var eased = 1 - (1 - t) * (1 - t);
                SidebarColumn.Width = new GridLength(from + (to - from) * eased);
            };
            timer.Start();
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

    // ═══════════════════════════════════════════════════════════════
    //  RESIZE GRIPS (для AllowsTransparency=True, WindowStyle=None)
    //
    //  Паттерн ReleaseCapture + PostMessage(WM_NCLBUTTONDOWN, HT*)
    //  — стандартный Win32-способ добавить ресайз в кастомный хром.
    //  SendMessage НЕ подходит для WindowStyle=None — он блокирует
    //  цикл сообщений, и окно не входит в режим sizing.
    // ═══════════════════════════════════════════════════════════════

    private const int WM_NCLBUTTONDOWN = 0x00A1;

    // HT* (hit-test) константы — соответствуют областям окна
    private const int HTTOP = 12;
    private const int HTBOTTOM = 15;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private static readonly Dictionary<string, int> ResizeEdges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Top"] = HTTOP,
        ["Bottom"] = HTBOTTOM,
        ["Left"] = HTLEFT,
        ["Right"] = HTRIGHT,
        ["TopLeft"] = HTTOPLEFT,
        ["TopRight"] = HTTOPRIGHT,
        ["BottomLeft"] = HTBOTTOMLEFT,
        ["BottomRight"] = HTBOTTOMRIGHT,
    };

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not FrameworkElement { Tag: string tag } || !ResizeEdges.TryGetValue(tag, out var htCode))
            return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ReleaseCapture();
        PostMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)htCode, IntPtr.Zero);
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double MinimumWindowWidth = 860;
    private const double MinimumWindowHeight = 520;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowMessageHook);
    }

    private static IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
            return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return IntPtr.Zero;

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return IntPtr.Zero;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;

        // Сохраняем WPF-ограничения минимального размера после перехвата сообщения.
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
            dpi = 96;
        minMaxInfo.MinTrackSize.X = (int)Math.Ceiling(MinimumWindowWidth * dpi / 96d);
        minMaxInfo.MinTrackSize.Y = (int)Math.Ceiling(MinimumWindowHeight * dpi / 96d);

        Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
