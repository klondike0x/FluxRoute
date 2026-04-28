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
using FluxRoute.Services;
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

    private void InstallUnifiedLogsTab()
    {
        try
        {
            AddLogsNavigationButton();
            MoveAboutNavigationToBottom();
            AddLogsPage();
            UpdateInjectedPageTitle();
        }
        catch (Exception ex)
        {
            _vm.Logs.Add($"[Логи] Не удалось создать вкладку логов: {ex.Message}");
        }
    }

    private void MoveAboutNavigationToBottom()
    {
        try
        {
            if (SidebarBorder.Child is WpfGrid { Tag: string tag } && tag == "SidebarWithBottomAbout")
                return;

            if (SidebarBorder.Child is not WpfStackPanel originalSidebar)
                return;

            var navGrid = originalSidebar.Children.OfType<WpfGrid>().FirstOrDefault();
            var navStack = navGrid?.Children.OfType<WpfStackPanel>()
                .FirstOrDefault(sp => sp.Children.OfType<WpfButton>().Any(IsAboutNavButton));

            if (navGrid is null || navStack is null)
                return;

            var aboutButton = navStack.Children.OfType<WpfButton>().FirstOrDefault(IsAboutNavButton);
            if (aboutButton is null)
                return;

            navStack.Children.Remove(aboutButton);

            // Remove spacer left by older patched builds, if it exists.
            foreach (var spacer in navStack.Children.OfType<FrameworkElement>()
                         .Where(e => Equals(e.Tag, "AboutNavSpacer"))
                         .ToList())
            {
                navStack.Children.Remove(spacer);
            }

            SidebarBorder.Child = null;

            var sidebarLayout = new WpfGrid { Tag = "SidebarWithBottomAbout" };
            sidebarLayout.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });
            sidebarLayout.RowDefinitions.Add(new WpfRowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sidebarLayout.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });

            WpfGrid.SetRow(originalSidebar, 0);
            sidebarLayout.Children.Add(originalSidebar);

            var bottomContainer = new WpfBorder
            {
                BorderBrush = BrushFrom("#21262D"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 4, 0, 4),
                Background = BrushFrom("#0D1117")
            };
            bottomContainer.Child = aboutButton;
            WpfGrid.SetRow(bottomContainer, 2);
            sidebarLayout.Children.Add(bottomContainer);

            SidebarBorder.Child = sidebarLayout;
        }
        catch (Exception ex)
        {
            _vm.Logs.Add($"[UI] Не удалось перенести пункт 'О программе' вниз: {ex.Message}");
        }
    }

    private static bool IsAboutNavButton(WpfButton button)
    {
        var parameter = button.CommandParameter?.ToString();
        if (parameter == "6")
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
        _navIndicatorBorder ??= EnumerateDescendants(this)
            .OfType<WpfBorder>()
            .FirstOrDefault(border => ReferenceEquals(border.RenderTransform, NavIndicatorTransform));

        if (_navIndicatorBorder is not null)
            _navIndicatorBorder.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private void AddLogsNavigationButton()
    {
        if (SidebarBorder.Child is not WpfStackPanel sidebar)
            return;

        var navGrid = sidebar.Children.OfType<WpfGrid>().FirstOrDefault();
        var navStack = navGrid?.Children.OfType<WpfStackPanel>()
            .FirstOrDefault(sp => sp.Children.OfType<WpfButton>().Count() >= 6);

        if (navStack is null)
            return;

        if (navStack.Children.OfType<WpfButton>().Any(b => Equals(b.CommandParameter, "7")))
            return;

        navStack.Children.Add(CreateLogsNavButton());
    }

    private WpfButton CreateLogsNavButton()
    {
        var button = new WpfButton
        {
            Height = 36,
            CommandParameter = "7"
        };

        WpfBindingOperations.SetBinding(button, WpfButton.CommandProperty, new WpfBinding("SelectTabCommand"));

        if (TryFindResource("NavBtn") is Style baseStyle)
        {
            var style = new Style(typeof(WpfButton), baseStyle);
            var trigger = new DataTrigger
            {
                Binding = new WpfBinding("SelectedTabIndex"),
                Value = 7
            };
            trigger.Setters.Add(new Setter(ForegroundProperty, BrushFrom("#E6EDF3")));
            trigger.Setters.Add(new Setter(BackgroundProperty, BrushFrom("#161B22")));
            style.Triggers.Add(trigger);
            button.Style = style;
        }

        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
        row.Children.Add(new WpfTextBlock
        {
            Text = "🧾",
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
        var pagesHost = FindPagesHost(this);
        if (pagesHost is null)
            return;

        if (pagesHost.Children.OfType<FrameworkElement>().Any(e => Equals(e.Tag, "UnifiedLogsPage")))
            return;

        pagesHost.Children.Add(CreateLogsPageBorder());
    }

    private WpfBorder CreateLogsPageBorder()
    {
        var border = new WpfBorder { Tag = "UnifiedLogsPage" };
        var style = new Style(typeof(WpfBorder));
        style.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        style.Setters.Add(new Setter(OpacityProperty, 0d));

        var trigger = new DataTrigger
        {
            Binding = new WpfBinding("SelectedTabIndex"),
            Value = 7
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
        var root = new WpfGrid { Margin = new Thickness(16, 12, 16, 12) };
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

        if (_vm.SelectedTabIndex == 7)
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
