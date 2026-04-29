using System.Windows;
using System.Windows.Controls;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FluxRoute.Helpers;

/// <summary>
/// Attached behavior for the custom overlay scrollbars declared in ScrollBarStyle.xaml.
/// The behavior fades scrollbars in while the user interacts with a ScrollViewer and fades
/// them out after a short idle period. It is intentionally UI-only and has no dependency on
/// application services or view models.
/// </summary>
public static class ScrollViewerBehavior
{
    public static readonly DependencyProperty AutoHideScrollBarProperty = DependencyProperty.RegisterAttached(
        "AutoHideScrollBar",
        typeof(bool),
        typeof(ScrollViewerBehavior),
        new PropertyMetadata(false, OnAutoHideScrollBarChanged));

    private static readonly DependencyProperty HideTimerProperty = DependencyProperty.RegisterAttached(
        "HideTimer",
        typeof(DispatcherTimer),
        typeof(ScrollViewerBehavior),
        new PropertyMetadata(null));

    public static bool GetAutoHideScrollBar(DependencyObject obj)
        => obj.GetValue(AutoHideScrollBarProperty) is true;

    public static void SetAutoHideScrollBar(DependencyObject obj, bool value)
        => obj.SetValue(AutoHideScrollBarProperty, value);

    private static DispatcherTimer? GetHideTimer(DependencyObject obj)
        => obj.GetValue(HideTimerProperty) as DispatcherTimer;

    private static void SetHideTimer(DependencyObject obj, DispatcherTimer? value)
        => obj.SetValue(HideTimerProperty, value);

    private static void OnAutoHideScrollBarChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
            return;

        if (e.OldValue is true)
            Detach(scrollViewer);

        if (e.NewValue is true)
            Attach(scrollViewer);
    }

    private static void Attach(ScrollViewer scrollViewer)
    {
        scrollViewer.Loaded += ScrollViewer_Loaded;
        scrollViewer.Unloaded += ScrollViewer_Unloaded;
        scrollViewer.MouseEnter += ScrollViewer_InteractionStarted;
        scrollViewer.MouseMove += ScrollViewer_InteractionStarted;
        scrollViewer.MouseWheel += ScrollViewer_InteractionStarted;
        scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

        var timer = new DispatcherTimer(DispatcherPriority.Background, scrollViewer.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SetScrollBarsOpacity(scrollViewer, 0.0);
        };

        SetHideTimer(scrollViewer, timer);
    }

    private static void Detach(ScrollViewer scrollViewer)
    {
        scrollViewer.Loaded -= ScrollViewer_Loaded;
        scrollViewer.Unloaded -= ScrollViewer_Unloaded;
        scrollViewer.MouseEnter -= ScrollViewer_InteractionStarted;
        scrollViewer.MouseMove -= ScrollViewer_InteractionStarted;
        scrollViewer.MouseWheel -= ScrollViewer_InteractionStarted;
        scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        var timer = GetHideTimer(scrollViewer);
        timer?.Stop();
        SetHideTimer(scrollViewer, null);
    }

    private static void ScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            SetScrollBarsOpacity(scrollViewer, 0.0, animate: false);
    }

    private static void ScrollViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            Detach(scrollViewer);
    }

    private static void ScrollViewer_InteractionStarted(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ShowThenScheduleHide(scrollViewer);
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ShowThenScheduleHide(scrollViewer);
    }

    private static void ShowThenScheduleHide(ScrollViewer scrollViewer)
    {
        SetScrollBarsOpacity(scrollViewer, 1.0);

        var timer = GetHideTimer(scrollViewer);
        timer?.Stop();
        timer?.Start();
    }

    private static void SetScrollBarsOpacity(ScrollViewer scrollViewer, double opacity, bool animate = true)
    {
        var verticalScrollBar = scrollViewer.Template.FindName("PART_VerticalScrollBar", scrollViewer) as WpfScrollBar;
        var horizontalScrollBar = scrollViewer.Template.FindName("PART_HorizontalScrollBar", scrollViewer) as WpfScrollBar;

        SetOpacity(verticalScrollBar, opacity, animate);
        SetOpacity(horizontalScrollBar, opacity, animate);
    }

    private static void SetOpacity(UIElement? element, double opacity, bool animate)
    {
        if (element is null)
            return;

        if (!animate)
        {
            element.Opacity = opacity;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = TimeSpan.FromMilliseconds(opacity > 0 ? 120 : 280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
