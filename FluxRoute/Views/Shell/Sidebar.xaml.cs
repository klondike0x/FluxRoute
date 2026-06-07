using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxRoute.Views.Shell;

public partial class Sidebar : System.Windows.Controls.UserControl
{
    // Slot constants for NavIndicator positioning
    private const double SlotHeight = 44.0; // Height=36 + Margin top=4 + bottom=4
    private const double AboutSlotY = 417.0; // Pinned bottom position

    public Sidebar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Animates the nav pill indicator to the given logical tab index.
    /// </summary>
    public void AnimateNavIndicator(int tabIndex)
    {
        double targetY;
        if (tabIndex == 7)
        {
            targetY = AboutSlotY;
        }
        else
        {
            int visualIndex = tabIndex switch
            {
                6 => 6,
                8 => 7,
                _ => tabIndex
            };
            targetY = visualIndex * SlotHeight;
        }

        var animation = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
