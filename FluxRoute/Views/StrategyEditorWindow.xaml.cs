using System.Windows;
using System.Windows.Input;

namespace FluxRoute.Views;

/// <summary>
/// Окно редактора .bat-файла стратегии.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class StrategyEditorWindow : Window
{
    public StrategyEditorWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
