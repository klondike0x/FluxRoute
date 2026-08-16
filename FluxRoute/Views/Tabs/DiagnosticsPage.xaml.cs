using FluxRoute.Services;
namespace FluxRoute.Views.Tabs;

public partial class DiagnosticsPage : System.Windows.Controls.UserControl
{
    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    private void TabHelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        TabHelp.Show((sender as System.Windows.Controls.Button)?.Tag as string);
    }}
