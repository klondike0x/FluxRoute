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
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingOperations = System.Windows.Data.BindingOperations;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushConverter = System.Windows.Media.BrushConverter;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVirtualizingPanel = System.Windows.Controls.VirtualizingPanel;
using WpfWrapPanel = System.Windows.Controls.WrapPanel;
using WpfBorder = System.Windows.Controls.Border;
using WpfOrientation = System.Windows.Controls.Orientation;
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
    private bool _isClosingConfirmed;
    private WpfListBox? _unifiedLogsList;
    private WpfTextBlock? _pageTitleTextBlock;


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

        // Unified logs tab
        InstallUnifiedLogsTab();
        _ = _vm.FilteredLogEntries;
        _vm.UnifiedLogEntries.CollectionChanged += UnifiedLogEntries_CollectionChanged;

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
                    "Завершить",
                    "Отмена",
                    isDanger: true))
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

        // Останавливаем TG WS Proxy
        _vm.StopTgProxyOnExit();

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
        {
            AnimateNavIndicator(_vm.SelectedTabIndex);
            UpdateInjectedPageTitle();
        }

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
            AddLogsPage();
            UpdateInjectedPageTitle();
        }
        catch (Exception ex)
        {
            _vm.Logs.Add($"[Логи] Не удалось создать вкладку логов: {ex.Message}");
        }
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

        _unifiedLogsList = new WpfListBox
        {
            Background = BrushFrom("#010409"),
            Foreground = BrushFrom("#C9D1D9"),
            BorderBrush = BrushFrom("#30363D"),
            BorderThickness = new Thickness(1),
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            DisplayMemberPath = "DisplayText"
        };
        WpfScrollViewer.SetVerticalScrollBarVisibility(_unifiedLogsList, WpfScrollBarVisibility.Auto);
        WpfScrollViewer.SetHorizontalScrollBarVisibility(_unifiedLogsList, WpfScrollBarVisibility.Auto);
        WpfVirtualizingPanel.SetIsVirtualizing(_unifiedLogsList, true);
        WpfVirtualizingPanel.SetVirtualizationMode(_unifiedLogsList, System.Windows.Controls.VirtualizationMode.Recycling);
        WpfBindingOperations.SetBinding(_unifiedLogsList, WpfListBox.ItemsSourceProperty, new WpfBinding("FilteredLogEntries"));

        WpfGrid.SetRow(_unifiedLogsList, 1);
        root.Children.Add(_unifiedLogsList);

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
        if (_unifiedLogsList is null || !_vm.LogsAutoScroll)
            return;

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var items = _unifiedLogsList.Items;
                if (items.Count > 0)
                    _unifiedLogsList.ScrollIntoView(items[items.Count - 1]);
            }
            catch
            {
            }
        }));
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
