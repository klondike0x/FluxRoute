using FluxRoute.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace FluxRoute.Views.Tabs;

/// <summary>
/// Каталог и управление модификациями.
/// </summary>
public partial class ModsPage : UserControl
{
    public ModsPage()
    {
        InitializeComponent();
    }

    private void TabHelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        TabHelp.Show((sender as System.Windows.Controls.Button)?.Tag as string);
    }}
