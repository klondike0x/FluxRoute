using System.Windows;
using System.Windows.Input;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
