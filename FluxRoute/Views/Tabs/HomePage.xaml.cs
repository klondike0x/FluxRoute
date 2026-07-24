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

    public void ApplyLayout(HomeLayoutMode mode)
    {
        var spec = AdaptiveHomeLayout.GetSpec(mode);

        WideGapColumn.Width = new GridLength(spec.DetailsGap);
        DetailsColumn.Width = new GridLength(spec.DetailsWidth);

        ServicesCard.Visibility = spec.ShowWideDetails ? Visibility.Visible : Visibility.Collapsed;
        ServicesCard.Opacity = spec.ShowWideDetails ? 1 : 0;

        CompactSummaryPanel.Visibility = spec.ShowCompactSummaryCards
            ? Visibility.Visible
            : Visibility.Collapsed;
        MetricsCard.Visibility = spec.ShowWideMonitor
            ? Visibility.Visible
            : Visibility.Collapsed;
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
