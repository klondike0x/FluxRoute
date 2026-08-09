using System.Windows;
using System.Windows.Input;

namespace FluxRoute.Views;

/// <summary>
/// Окно онбординга при первом запуске FluxRoute.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// DragMove для окна без заголовка.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
