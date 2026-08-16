using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace FluxRoute.Views;

public partial class DonateWindow : Window
{
    public DonateWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Ton_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://t.me/fluxroute") { UseShellExecute = true });
        Close();
    }

    private void Bitcoin_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://t.me/fluxroute") { UseShellExecute = true });
        Close();
    }

    private void GitHub_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/klondike0x/FluxRoute") { UseShellExecute = true });
        Close();
    }
}
