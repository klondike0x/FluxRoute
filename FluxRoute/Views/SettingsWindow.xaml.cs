using System.Collections.Specialized;
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

        vm.ServiceLogs.CollectionChanged += ServiceLogs_CollectionChanged;
        Closed += (_, _) => vm.ServiceLogs.CollectionChanged -= ServiceLogs_CollectionChanged;
    }

    private void ServiceLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ServiceLogScroll?.ScrollToEnd();
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
