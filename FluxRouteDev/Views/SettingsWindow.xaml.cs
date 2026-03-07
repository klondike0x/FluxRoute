using System.Windows;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
