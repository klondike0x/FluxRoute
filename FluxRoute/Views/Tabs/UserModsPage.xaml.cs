using UserControl = System.Windows.Controls.UserControl;
using FluxRoute.ViewModels;

namespace FluxRoute.Views.Tabs;

/// <summary>
/// Страница управления модами.
/// </summary>
public partial class UserModsPage : UserControl
{
    public UserModsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Загружает список модов при первом открытии страницы.
    /// </summary>
    private async void UserModsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ModsViewModel vm)
            await vm.LoadModsCommand.ExecuteAsync(null);
    }
}
