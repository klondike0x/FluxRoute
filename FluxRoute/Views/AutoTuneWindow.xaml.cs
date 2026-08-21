using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class AutoTuneWindow : Window
{
    private ServiceViewModel? _viewModel;
    private bool _closeRequested;

    public AutoTuneWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as ServiceViewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _closeRequested = true;
        if (_viewModel?.AutoTuneResultVisible != true &&
            _viewModel?.CancelAutoTuneCommand.CanExecute(null) == true)
            _viewModel.CancelAutoTuneCommand.Execute(null);
        Close();
    }

    private void CloseResultButton_Click(object sender, RoutedEventArgs e)
    {
        _closeRequested = true;
        Close();
    }

    private void ApplyBestButton_Click(object sender, RoutedEventArgs e)
    {
        _closeRequested = true;
        if (_viewModel?.ApplyBestAutoTuneCommand.CanExecute(null) == true)
            _viewModel.ApplyBestAutoTuneCommand.Execute(null);
        else
            Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeRequested)
            return;

        _closeRequested = true;
        if (_viewModel?.AutoTuneResultVisible != true &&
            _viewModel?.CancelAutoTuneCommand.CanExecute(null) == true)
            _viewModel.CancelAutoTuneCommand.Execute(null);
    }

    public void CloseFromViewModel()
    {
        _closeRequested = true;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
