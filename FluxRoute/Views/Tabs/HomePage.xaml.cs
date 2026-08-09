using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FluxRoute.ViewModels;

namespace FluxRoute.Views.Tabs;

public partial class HomePage : System.Windows.Controls.UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer _idlePulseTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1480)
    };

    public HomePage()
    {
        InitializeComponent();
    }

    // ═══ v1.8.0: Переход на вкладку TG Proxy по клику на карточку ═══
    private void TgProxyCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedTabIndex = 1; // TG Proxy вкладка
    }

    // ═══ v1.8.0: Копирование ссылки TG Proxy ═══
    private void CopyTgProxyLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var link = $"https://t.me/proxy?server={vm.TgProxyHost}&port={vm.TgProxyPort}&secret={vm.TgProxySecret}";
            System.Windows.Clipboard.SetText(link);
            vm.Logs.Add("[TG Proxy] Ссылка скопирована в буфер обмена");
        }
    }

    public void ApplyLayout(HomeLayoutMode mode)
    {
        bool wide = mode == HomeLayoutMode.Wide;
        MainDetailsColumn.Width = new GridLength(wide ? 280 : 228);

        // In the compact window, move the proxy and strategy actions below the hero.
        if (wide)
        {
            CompactBottomPanel.Visibility = Visibility.Collapsed;
            PlaceIn(DetailsPanel, TgProxyCard, 0);
            PlaceIn(DetailsPanel, StrategyActionsPanel, 1);
        }
        else
        {
            PlaceIn(CompactBottomPanel, TgProxyCard);
            PlaceIn(CompactBottomPanel, StrategyActionsPanel);
            CompactBottomPanel.Visibility = Visibility.Visible;
        }

        var heroScale = wide ? 1.0 : 0.86;
        HeroScaleTransform.ScaleX = heroScale;
        HeroScaleTransform.ScaleY = heroScale;

        // Keep the right-side controls at their normal size in both modes.
        DetailsScaleTransform.ScaleX = 1.0;
        DetailsScaleTransform.ScaleY = 1.0;
        NetworkCard.Padding = wide
            ? new Thickness(14, 11, 14, 11)
            : new Thickness(10, 8, 10, 8);
        NetworkCard.Margin = wide
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0, 6, 0, 0);
        OrchestratorCard.Padding = wide
            ? new Thickness(16, 14, 16, 14)
            : new Thickness(12, 10, 12, 10);
        OrchestratorCard.Margin = wide
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0, 6, 0, 0);
        DetailsScrollViewer.VerticalScrollBarVisibility = wide
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    private static void PlaceIn(System.Windows.Controls.Panel target, FrameworkElement element, int index = -1)
    {
        if (element.Parent is System.Windows.Controls.Panel currentParent)
            currentParent.Children.Remove(element);

        if (index < 0 || index > target.Children.Count)
            target.Children.Add(element);
        else
            target.Children.Insert(index, element);
    }

    public void PlayWave(bool outward, double strength, int duration)
    {
        if (WaveRing1 == null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double startScale = outward ? 0.85 : 1.22;
        double endScale   = outward ? 1.22 : 0.78;

        int[] delays = { 0, 80, 160 };
        double[] alphas = { strength, strength * 0.78, strength * 0.52 };

        var rings = new System.Windows.Shapes.Ellipse[] { WaveRing1, WaveRing2, WaveRing3 };
        var scales = new ScaleTransform[] { WaveRing1Scale, WaveRing2Scale, WaveRing3Scale };

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

    public void StartIdlePulse()
    {
        _idlePulseTimer.Stop();
        _idlePulseTimer.Interval = TimeSpan.FromMilliseconds(2400);
        _idlePulseTimer.Tick -= OnIdlePulseTick;
        _idlePulseTimer.Tick += OnIdlePulseTick;
        _idlePulseTimer.Start();
        PlayWave(outward: true, strength: 0.38, duration: 2200);
    }

    public void StopIdlePulse()
    {
        _idlePulseTimer.Stop();
        _idlePulseTimer.Tick -= OnIdlePulseTick;
    }

    private void OnIdlePulseTick(object? sender, EventArgs e)
    {
        // Проверяем через DataContext, запущен ли сервис
        if (DataContext is FluxRoute.ViewModels.MainViewModel vm && vm.IsRunning)
            PlayWave(outward: true, strength: 0.38, duration: 2200);
    }
}
