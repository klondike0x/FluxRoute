using System.Windows;
using System.Windows.Input;

namespace FluxRoute.Views.Shell;

public partial class TitleBar : System.Windows.Controls.UserControl
{
    /// <summary>Raised when the user clicks the minimize button.</summary>
    public event RoutedEventHandler? MinimizeRequested;

    /// <summary>Raised when the user clicks the close button.</summary>
    public event RoutedEventHandler? CloseRequested;

    public TitleBar()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            Window.GetWindow(this)?.DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => MinimizeRequested?.Invoke(this, e);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, e);
}
