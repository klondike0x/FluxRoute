using System.Windows;
using System.Windows.Input;
using FluxRoute.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Window = System.Windows.Window;

namespace FluxRoute.Views.Tray;

public partial class TrayPopupWindow : Window
{
    private bool _closeScheduled;

    public TrayPopupWindow(TrayPopupViewModel viewModel, bool closeOnDeactivate = true)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        if (closeOnDeactivate)
            Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closeScheduled)
            return;

        _closeScheduled = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (IsLoaded)
                Close();
        }));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
}
