using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

/// <summary>
/// Отдельное окно подбора стратегий с живым прогрессом и результатами.
/// </summary>
public partial class StrategyScanWindow : Window
{
    private MainViewModel? _viewModel;

    public StrategyScanWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        _viewModel = vm;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CancelScanIfRunning();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CancelScanIfRunning();
    }

    private void CancelScanIfRunning()
    {
        if (_viewModel?.IsScanning == true &&
            _viewModel.CancelScanCommand.CanExecute(null))
        {
            _viewModel.CancelScanCommand.Execute(null);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}