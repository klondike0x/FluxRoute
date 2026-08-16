using System;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Threading;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private AutoTuneWindow? _autoTuneWindow;

    private void ShowAutoTuneWindow()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_autoTuneWindow is { IsVisible: true } visibleWindow)
            {
                visibleWindow.Activate();
                return;
            }

            var window = new AutoTuneWindow
            {
                DataContext = Service
            };

            if (Application.Current.MainWindow is { IsLoaded: true } owner)
                window.Owner = owner;

            _autoTuneWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_autoTuneWindow, window))
                    _autoTuneWindow = null;
            };

            window.ShowDialog();
        }), DispatcherPriority.Normal);
    }

    private void HideAutoTuneWindow()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _autoTuneWindow?.CloseFromViewModel();
        }), DispatcherPriority.Normal);
    }
}
