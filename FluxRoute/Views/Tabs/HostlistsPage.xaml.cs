using System.Windows;
using FluxRoute.Services;
using FluxRoute.ViewModels;
using FluxRoute.Views;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FluxRoute.Views.Tabs;

/// <summary>
/// Вкладка Хостлисты — редактирование списков доменов.
/// v1.7.0: UI-Redesign
/// </summary>
public partial class HostlistsPage : WpfUserControl
{
    public HostlistsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is HostlistsViewModel vm)
            vm.UnsavedChangesPrompt = PromptUnsavedChanges;
    }

    private static HostlistUnsavedChangesDecision PromptUnsavedChanges()
    {
        return CustomDialog.ShowUnsavedChanges() switch
        {
            CustomDialogChoice.Confirm => HostlistUnsavedChangesDecision.Save,
            CustomDialogChoice.Alternate => HostlistUnsavedChangesDecision.Discard,
            _ => HostlistUnsavedChangesDecision.Stay
        };
    }

    /// <summary>
    /// Загружает файлы хостлистов при активации вкладки.
    /// </summary>
    public void Refresh()
    {
        if (DataContext is HostlistsViewModel vm)
        {
            vm.UnsavedChangesPrompt = PromptUnsavedChanges;
            vm.LoadHostlistFiles();
        }
    }

    private void TabHelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        TabHelp.Show((sender as System.Windows.Controls.Button)?.Tag as string);
    }}
