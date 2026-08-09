using FluxRoute.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FluxRoute.Views.Tabs;

/// <summary>
/// Вкладка Хостлисты — редактирование списков доменов.
/// v1.8.0: UI-Redesign
/// </summary>
public partial class HostlistsPage : WpfUserControl
{
    public HostlistsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Загружает файлы хостлистов при активации вкладки.
    /// </summary>
    public void Refresh()
    {
        if (DataContext is HostlistsViewModel vm)
            vm.LoadHostlistFiles();
    }
}
