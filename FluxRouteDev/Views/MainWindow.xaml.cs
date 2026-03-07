using System.Windows;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Открываем настройки когда ViewModel просит
        _vm.OpenSettingsRequested += OnOpenSettingsRequested;
        _vm.OpenAboutRequested += OnOpenAboutRequested;
    }


    private void OnOpenAboutRequested(object? sender, EventArgs e)
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow() { Owner = this };
        _aboutWindow.Show();
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_vm) { Owner = this };
        _settingsWindow.Show();
    }
}
