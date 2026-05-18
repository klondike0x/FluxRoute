using System.IO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using FluxRoute.AI.Services;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.ViewModels;
using Microsoft.Extensions.Logging;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingOperations = System.Windows.Data.BindingOperations;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushConverter = System.Windows.Media.BrushConverter;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfItemsControl = System.Windows.Controls.ItemsControl;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWrapPanel = System.Windows.Controls.WrapPanel;
using WpfBorder = System.Windows.Controls.Border;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRowDefinition = System.Windows.Controls.RowDefinition;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfBindingMode = System.Windows.Data.BindingMode;
using WpfUpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger;
using WpfScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;


namespace FluxRoute.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIconService _trayIcon;
    private readonly ILogger<MainWindow>? _logger;
    private bool _isClosingConfirmed;
    private WpfTextBox? _unifiedLogsTextBox;
    private WpfTextBlock? _pageTitleTextBlock;
    private WpfBorder? _navIndicatorBorder;

    // ── Wave pulse animation (Zapret-Hub style) ──
    private readonly System.Windows.Threading.DispatcherTimer _idlePulseTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1480)
    };
    private bool _waveBusy = false;


    // Parameterless constructor is intentionally kept for the WPF designer
    // and as a safe fallback if the window is ever instantiated outside DI.
    public MainWindow()
        : this(CreateDesignTimeViewModel(), new TrayIconService(), null)
    {
    }

    private static MainViewModel CreateDesignTimeViewModel()
    {
        var settings = new SettingsService();
        var dir = Path.GetDirectoryName(settings.SettingsPath)!;
        var registry = new AiStrategyRegistry(Path.Combine(dir, "fluxroute-ai-strategies.json"));
        registry.Load();
        var history = new AiHistoryStore(Path.Combine(dir, "fluxroute-ai-history.jsonl"));
        var materializer = new BatMaterializer();
        var fingerprints = new NetworkFingerprintProvider();
        return new MainViewModel(
            settings,
            new UpdaterService(),
            new AppUpdaterService(),
            new ConnectivityChecker(),
            fingerprints,
            new NetworkChangeWatcher(fingerprints),
            registry,
            history,
            new BanditSelector(registry, new Random()),
            new StrategyEvolver(registry, history, materializer,
                () => Path.Combine(AppContext.BaseDirectory, "engine"),
                () => settings.Load().Ai),
            materializer);
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
        InstallUnifiedLogsTab();
        _ = _vm.FilteredLogEntries;
        _vm.UnifiedLogEntries.CollectionChanged += UnifiedLogEntries_CollectionChanged;

        Loaded += (_, _) =>
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.BeginInvoke(new Action(HideLegacyInlineLogControls));
        };

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            AnimateNavIndicator(_vm.SelectedTabIndex);
            UpdateInjectedPageTitle();
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
    {
        // Tab 7 (About) is pinned to the bottom in a separate StackPanel (Margin.Top=416)
        // Center Y = 416 (StackPanel) + 9 (separator) + 4 (Grid margin) + 18 (icon center) = 447
        // Pill Margin.Top=20, Height=20 → Y = 447 - 10 - 20 = 417
        double targetY;
        if (tabIndex == 7)
        {
            targetY = 417;
        }
        else
        {
            // Map logical tab index to visual slot in StackPanel
            // Slot order: 0,1,2,3,4,5, 8(logs)→6, 6(settings)→7
            int visualIndex = tabIndex switch
            {
                8 => 6,
                6 => 7,
                _ => tabIndex
            };
            // Each slot: Height=36 + Margin top=4 + bottom=4 = 44px per slot
            // Pill Margin.Top=20 already positions it at slot-0 center (8 top + 12 center offset)
            // TranslateTransform.Y is additive offset from that base position
            targetY = visualIndex * 44;
        }

        var animation = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void AnimateSidebar(bool expanded)
    {
        // No-op in v1.5.0: sidebar is fixed-width icon-only; no expand/collapse animation.
    }

    // ── Wave pulse (Zapret-Hub style) ────────────────────────────────────────

    /// <summary>
    /// Plays a 3-ring expanding (outward=true) or contracting (outward=false) wave.
    /// strength: 0..1 opacity peak. duration: ms.
    /// </summary>
    private void PlayWave(bool outward, double strength, int duration)
    {
        if (WaveRing1 == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double startScale = outward ? 0.85 : 1.22;
        double endScale   = outward ? 1.22 : 0.78;

        // Stagger: ring 2 offset +80ms, ring 3 offset +160ms
        int[] delays = { 0, 80, 160 };
        double[] alphas = { strength, strength * 0.78, strength * 0.52 };

        var rings = new[] { WaveRing1, WaveRing2, WaveRing3 };
        var scales = new[] { WaveRing1Scale, WaveRing2Scale, WaveRing3Scale };

        for (int i = 0; i < 3; i++)
        {
            var ring = rings[i];
            var scale = scales[i];
            double alpha = alphas[i];
            int delay = delays[i];

            ring.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ring.Opacity = 0;
            scale.ScaleX = startScale;
            scale.ScaleY = startScale;

            var opacityAnim = new DoubleAnimationUsingKeyFrames();
            opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame(alpha, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration * 0.08)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration)), new CubicEase { EasingMode = EasingMode.EaseIn }));
            ring.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

            var scaleXAnim = new DoubleAnimation(startScale, endScale,
                new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = ease
            };
            var scaleYAnim = new DoubleAnimation(startScale, endScale,
                new Duration(TimeSpan.FromMilliseconds(duration)))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = ease
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }
    }

    private void StartIdlePulse()
    {
        _idlePulseTimer.Stop();
        _idlePulseTimer.Interval = TimeSpan.FromMilliseconds(2400);
        _idlePulseTimer.Tick -= OnIdlePulseTick;
        _idlePulseTimer.Tick += OnIdlePulseTick;
        _idlePulseTimer.Start();
        // Fire immediately
        PlayWave(outward: true, strength: 0.38, duration: 2200);
    }

    private void StopIdlePulse()
    {
        _idlePulseTimer.Stop();
        _idlePulseTimer.Tick -= OnIdlePulseTick;
    }

    private void OnIdlePulseTick(object? sender, EventArgs e)
    {
        if (_vm.IsRunning)
            PlayWave(outward: true, strength: 0.38, duration: 2200);
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

    private void InstallUnifiedLogsTab()
    {
        try
        {
            // v1.5.0 redesign: nav buttons and logs page are fully declared in XAML.
            // Only locate the existing UnifiedLogsTextBox; no tree injection needed.
            var hasExistingLogsPage = TryUseExistingUnifiedLogsPage();
            if (!hasExistingLogsPage)
                AddLogsPage();

            UpdateInjectedPageTitle();
        }
        catch (Exception ex)
        {
            _vm.Logs.Add($"[Логи] Не удалось создать вкладку логов: {ex.Message}");
        }
    }

    private bool TryUseExistingUnifiedLogsPage()
    {
        if (FindName("UnifiedLogsTextBox") is not WpfTextBox existingLogsTextBox)
            return false;

        _unifiedLogsTextBox = existingLogsTextBox;

        // Помечаем корневой Border существующей XAML-страницы как UnifiedLogsPage.
        // Это нужно, чтобы HideLegacyInlineLogControls не принимал кнопки
        // "Очистить/Скопировать/Сохранить" на новой вкладке за старые inline-логи.
        if (FindUnifiedLogsPageRoot(existingLogsTextBox) is { } pageRoot)
            pageRoot.Tag = "UnifiedLogsPage";

        return true;
    }

    private static FrameworkElement? FindUnifiedLogsPageRoot(DependencyObject start)
    {
        DependencyObject? current = start;

        for (var i = 0; i < 16 && current is not null; i++)
        {
            var parent = GetParentObject(current);
            if (parent is WpfBorder border && current is WpfGrid grid && grid.RowDefinitions.Count >= 2)
                return border;

            current = parent;
        }

        return null;
    }

    private void MoveAboutNavigationToBottom()
    {
        // No-op in v1.5.0: the About nav item is already placed at the bottom in XAML.
    }

    private static bool IsAboutNavButton(WpfButton button)
    {
        var parameter = button.CommandParameter?.ToString();
        if (parameter == "7")
            return true;

        return ContainsText(button, "О программе");
    }

    private static bool ContainsText(DependencyObject root, string text)
    {
        if (root is WpfTextBlock textBlock &&
            string.Equals(textBlock.Text?.Trim(), text, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (ContainsText(child, text))
                return true;
        }

        try
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                if (ContainsText(VisualTreeHelper.GetChild(root, i), text))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void SetNavIndicatorVisible(bool visible)
    {
        // Pill is always visible; toggle no longer needed
        _ = visible;
    }

    private void AddLogsNavigationButton()
    {
        // No-op in v1.5.0: the Logs nav button is already declared in XAML.
    }

    private WpfButton CreateLogsNavButton()
    {
        var button = new WpfButton
        {
            Height = 36,
            CommandParameter = "8"
        };

        WpfBindingOperations.SetBinding(button, WpfButton.CommandProperty, new WpfBinding("SelectTabCommand"));

        if (TryFindResource("NavBtn") is Style baseStyle)
        {
            var style = new Style(typeof(WpfButton), baseStyle);
            var trigger = new DataTrigger
            {
                Binding = new WpfBinding("SelectedTabIndex"),
                Value = 8
            };
            trigger.Setters.Add(new Setter(ForegroundProperty, BrushFrom("#E6EDF3")));
            trigger.Setters.Add(new Setter(BackgroundProperty, BrushFrom("#161B22")));
            style.Triggers.Add(trigger);
            button.Style = style;
        }

        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
        row.Children.Add(new WpfTextBlock
        {
            Text = "≡",
            Width = 20,
            TextAlignment = System.Windows.TextAlignment.Center
        });

        var text = new WpfTextBlock
        {
            Text = "Логи",
            Margin = new Thickness(6, 0, 0, 0)
        };

        var visibilityBinding = new WpfBinding("IsSidebarExpanded");
        if (TryFindResource("BoolToVis") is System.Windows.Data.IValueConverter converter)
            visibilityBinding.Converter = converter;

        WpfBindingOperations.SetBinding(text, VisibilityProperty, visibilityBinding);
        row.Children.Add(text);
        button.Content = row;

        return button;
    }

    private void AddLogsPage()
    {
        if (_unifiedLogsTextBox is not null)
            return;

        var pagesHost = FindPagesHost(this);
        if (pagesHost is null)
            return;

        if (pagesHost.Children.OfType<FrameworkElement>().Any(e => Equals(e.Tag, "UnifiedLogsPage")))
            return;

        pagesHost.Children.Add(CreateLogsPageBorder());
    }

    private WpfBorder CreateLogsPageBorder()
    {
        var border = new WpfBorder
        {
            Tag = "UnifiedLogsPage",
            Background = BrushFrom("#0D1117")
        };
        var style = new Style(typeof(WpfBorder));
        style.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        style.Setters.Add(new Setter(OpacityProperty, 0d));

        var trigger = new DataTrigger
        {
            Binding = new WpfBinding("SelectedTabIndex"),
            Value = 8
        };
        trigger.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        trigger.Setters.Add(new Setter(OpacityProperty, 1d));
        style.Triggers.Add(trigger);

        border.Style = style;
        border.Child = CreateLogsPageContent();
        return border;
    }

    private UIElement CreateLogsPageContent()
    {
        var root = new WpfGrid
        {
            Margin = new Thickness(16, 12, 16, 12),
            Background = BrushFrom("#0D1117"),
            ClipToBounds = true
        };
        root.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new WpfRowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topCard = new WpfBorder
        {
            Background = BrushFrom("#0D1117"),
            BorderBrush = BrushFrom("#30363D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var topPanel = new WpfStackPanel();
        topPanel.Children.Add(new WpfTextBlock
        {
            Text = "Логи",
            Foreground = BrushFrom("#4FC3F7"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        topPanel.Children.Add(new WpfTextBlock
        {
            Text = "Здесь собраны события приложения, оркестратора, проверки профилей, winws.exe, TG WS Proxy, обновлений и сервиса.",
            Foreground = BrushFrom("#8B949E"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var controls = new WpfWrapPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        controls.Children.Add(new WpfTextBlock
        {
            Text = "Тип:",
            Foreground = BrushFrom("#8B949E"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var categoryCombo = new WpfComboBox
        {
            Width = 210,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 8)
        };
        WpfBindingOperations.SetBinding(categoryCombo, WpfComboBox.ItemsSourceProperty, new WpfBinding("LogCategoryFilters"));
        WpfBindingOperations.SetBinding(categoryCombo, WpfComboBox.SelectedItemProperty, new WpfBinding("SelectedLogCategory") { Mode = WpfBindingMode.TwoWay, UpdateSourceTrigger = WpfUpdateSourceTrigger.PropertyChanged });
        controls.Children.Add(categoryCombo);

        controls.Children.Add(new WpfTextBlock
        {
            Text = "Поиск:",
            Foreground = BrushFrom("#8B949E"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var searchBox = new WpfTextBox
        {
            Width = 220,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 8),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        WpfBindingOperations.SetBinding(searchBox, WpfTextBox.TextProperty, new WpfBinding("LogSearchText") { Mode = WpfBindingMode.TwoWay, UpdateSourceTrigger = WpfUpdateSourceTrigger.PropertyChanged });
        controls.Children.Add(searchBox);

        var errorsOnly = new WpfCheckBox
        {
            Content = "Только ошибки",
            Foreground = BrushFrom("#C9D1D9"),
            Margin = new Thickness(0, 4, 12, 8),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        WpfBindingOperations.SetBinding(errorsOnly, WpfCheckBox.IsCheckedProperty, new WpfBinding("LogsErrorsOnly") { Mode = WpfBindingMode.TwoWay });
        controls.Children.Add(errorsOnly);

        var autoScroll = new WpfCheckBox
        {
            Content = "Автопрокрутка",
            Foreground = BrushFrom("#C9D1D9"),
            Margin = new Thickness(0, 4, 12, 8),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        WpfBindingOperations.SetBinding(autoScroll, WpfCheckBox.IsCheckedProperty, new WpfBinding("LogsAutoScroll") { Mode = WpfBindingMode.TwoWay });
        controls.Children.Add(autoScroll);

        controls.Children.Add(CreateLogActionButton("Очистить", "ClearUnifiedLogsCommand"));
        controls.Children.Add(CreateLogActionButton("Скопировать", "CopyUnifiedLogsCommand"));
        controls.Children.Add(CreateLogActionButton("Сохранить", "SaveUnifiedLogsCommand"));

        topPanel.Children.Add(controls);
        topCard.Child = topPanel;
        WpfGrid.SetRow(topCard, 0);
        root.Children.Add(topCard);

        _unifiedLogsTextBox = new WpfTextBox
        {
            Background = BrushFrom("#010409"),
            Foreground = BrushFrom("#C9D1D9"),
            BorderBrush = BrushFrom("#30363D"),
            BorderThickness = new Thickness(1),
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
            IsReadOnly = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WpfScrollBarVisibility.Disabled
        };
        WpfBindingOperations.SetBinding(_unifiedLogsTextBox, WpfTextBox.TextProperty, new WpfBinding("UnifiedLogsText") { Mode = WpfBindingMode.OneWay });

        WpfGrid.SetRow(_unifiedLogsTextBox, 1);
        root.Children.Add(_unifiedLogsTextBox);

        return root;
    }

    private WpfButton CreateLogActionButton(string text, string commandPath)
    {
        var button = new WpfButton
        {
            Content = text,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 8, 8),
            Foreground = BrushFrom("#C9D1D9"),
            Background = BrushFrom("#161B22"),
            BorderBrush = BrushFrom("#30363D"),
            BorderThickness = new Thickness(1)
        };

        if (TryFindResource("TermBtn") is Style termButtonStyle)
            button.Style = termButtonStyle;

        WpfBindingOperations.SetBinding(button, WpfButton.CommandProperty, new WpfBinding(commandPath));
        return button;
    }

    private void UnifiedLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_unifiedLogsTextBox is null || !_vm.LogsAutoScroll)
            return;

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _unifiedLogsTextBox.CaretIndex = _unifiedLogsTextBox.Text.Length;
                _unifiedLogsTextBox.ScrollToEnd();
            }
            catch
            {
            }
        }));
    }

    private void HideLegacyInlineLogControls()
    {
        try
        {
            var targets = new HashSet<FrameworkElement>();

            foreach (var element in EnumerateDescendants(this).OfType<FrameworkElement>())
            {
                if (IsInUnifiedLogsPage(element))
                    continue;

                if (HasLegacyLogBinding(element))
                {
                    targets.Add(FindLegacyLogContainer(element));
                    continue;
                }

                if (IsLegacyLogCommandButton(element))
                    targets.Add(element);
            }

            foreach (var target in targets.Where(t => !IsInUnifiedLogsPage(t)))
            {
                target.Visibility = Visibility.Collapsed;
                HideNeighborLegacyLogHeaders(target);
            }
        }
        catch (Exception ex)
        {
            _vm.Logs.Add($"[Логи] Не удалось скрыть старые блоки логов: {ex.Message}");
        }
    }

    private static bool HasLegacyLogBinding(FrameworkElement element)
    {
        if (element is WpfItemsControl itemsControl)
        {
            var path = GetBindingPath(itemsControl, WpfItemsControl.ItemsSourceProperty);
            if (IsLegacyLogCollectionPath(path))
                return true;
        }

        if (element is WpfTextBox textBox)
        {
            var path = GetBindingPath(textBox, WpfTextBox.TextProperty);
            if (IsLegacyLogTextPath(path))
                return true;
        }

        return false;
    }

    private static bool IsLegacyLogCommandButton(FrameworkElement element)
    {
        if (element is not WpfButton button)
            return false;

        var commandPath = GetBindingPath(button, WpfButton.CommandProperty);
        if (!string.IsNullOrWhiteSpace(commandPath) &&
            (commandPath.Equals("ShowLogsCommand", StringComparison.OrdinalIgnoreCase) ||
             commandPath.StartsWith("Clear", StringComparison.OrdinalIgnoreCase) &&
             commandPath.Contains("Logs", StringComparison.OrdinalIgnoreCase)))
            return true;

        var content = button.Content?.ToString() ?? string.Empty;
        return content.Contains("Очистить лог", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Показать логи", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyLogCollectionPath(string? path)
    {
        return path is "Logs" or "RecentLogs" or "UpdateLogs" or "ServiceLogs" or "TgProxyLogs" or "OrchestratorLogs";
    }

    private static bool IsLegacyLogTextPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.Equals("UnifiedLogsText", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("LogSearchText", StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Equals("LogsText", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("LogText", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("LogsText", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("LogText", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetBindingPath(DependencyObject element, DependencyProperty property)
    {
        return WpfBindingOperations.GetBinding(element, property)?.Path?.Path;
    }

    private static FrameworkElement FindLegacyLogContainer(FrameworkElement element)
    {
        FrameworkElement fallback = element;
        DependencyObject? current = element;

        for (var i = 0; i < 8; i++)
        {
            var parent = GetParentObject(current);
            if (parent is null)
                break;

            if (parent is WpfBorder border && !IsInUnifiedLogsPage(border))
                return border;

            if (parent is WpfScrollViewer scrollViewer && !IsInUnifiedLogsPage(scrollViewer))
                fallback = scrollViewer;

            current = parent;
        }

        return fallback;
    }

    private static void HideNeighborLegacyLogHeaders(FrameworkElement target)
    {
        DependencyObject? current = target;

        for (var level = 0; level < 4; level++)
        {
            var parent = GetParentObject(current);
            if (parent is null)
                return;

            if (parent is WpfPanel panel)
            {
                var index = current is UIElement currentElement ? panel.Children.IndexOf(currentElement) : -1;
                if (index >= 0)
                {
                    for (var i = index - 1; i >= 0 && i >= index - 5; i--)
                    {
                        if (panel.Children[i] is WpfTextBlock textBlock && IsLegacyLogHeaderText(textBlock.Text))
                            textBlock.Visibility = Visibility.Collapsed;

                        if (panel.Children[i] is WpfButton button && IsLegacyLogCommandButton(button))
                            button.Visibility = Visibility.Collapsed;
                    }
                }
            }

            current = parent;
        }
    }

    private static bool IsLegacyLogHeaderText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim();
        return normalized.Contains("ЛОГ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("ЖУРНАЛ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("СОБЫТ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInUnifiedLogsPage(DependencyObject element)
    {
        DependencyObject? current = element;

        for (var i = 0; i < 32 && current is not null; i++)
        {
            if (current is FrameworkElement { Tag: "UnifiedLogsPage" })
                return true;

            current = GetParentObject(current);
        }

        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject? element)
    {
        if (element is null)
            return null;

        var logicalParent = LogicalTreeHelper.GetParent(element);
        if (logicalParent is not null)
            return logicalParent;

        try
        {
            return VisualTreeHelper.GetParent(element);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        var visited = new HashSet<DependencyObject>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && visited.Count < 10000)
        {
            var (node, depth) = queue.Dequeue();
            if (!visited.Add(node))
                continue;

            yield return node;

            if (depth >= 80)
                continue;

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                queue.Enqueue((child, depth + 1));

            try
            {
                var visualChildrenCount = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < visualChildrenCount; i++)
                    queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
            }
            catch
            {
            }
        }
    }

    private void UpdateInjectedPageTitle()
    {
        _pageTitleTextBlock ??= FindTextBlockBoundTo(this, "SelectedTabName");
        if (_pageTitleTextBlock is null)
            return;

        if (_vm.SelectedTabIndex == 8)
        {
            _pageTitleTextBlock.SetCurrentValue(WpfTextBlock.TextProperty, "ЛОГИ");
        }
        else
        {
            WpfBindingOperations.SetBinding(_pageTitleTextBlock, WpfTextBlock.TextProperty, new WpfBinding("SelectedTabName"));
        }
    }

    private static WpfGrid? FindPagesHost(DependencyObject root)
    {
        WpfGrid? best = null;
        var bestCount = 0;

        void Visit(DependencyObject node, int depth)
        {
            if (depth > 80)
                return;

            if (node is WpfGrid grid)
            {
                var borderCount = grid.Children.OfType<WpfBorder>().Count();
                if (borderCount > bestCount)
                {
                    best = grid;
                    bestCount = borderCount;
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                Visit(child, depth + 1);
        }

        Visit(root, 0);
        return bestCount >= 4 ? best : null;
    }

    private static WpfTextBlock? FindTextBlockBoundTo(DependencyObject root, string bindingPath)
    {
        WpfTextBlock? result = null;

        void Visit(DependencyObject node, int depth)
        {
            if (result is not null || depth > 80)
                return;

            if (node is WpfTextBlock textBlock)
            {
                var binding = WpfBindingOperations.GetBinding(textBlock, WpfTextBlock.TextProperty);
                if (binding?.Path?.Path == bindingPath)
                {
                    result = textBlock;
                    return;
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                Visit(child, depth + 1);
        }

        Visit(root, 0);
        return result;
    }

    private static WpfBrush BrushFrom(string hex)
    {
        return (WpfBrush)new WpfBrushConverter().ConvertFromString(hex)!;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }


}
