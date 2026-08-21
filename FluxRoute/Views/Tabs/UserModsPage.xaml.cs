using FluxRoute.Services;
using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using FluxRoute.Core.Models;
using FluxRoute.ViewModels;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace FluxRoute.Views.Tabs;

/// <summary>
/// Страница управления модами.
/// </summary>
public partial class UserModsPage : UserControl
{
    private ModInfo? _draggedMod;
    private WpfPoint _dragStartPoint;

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

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModInfo mod })
        {
            _draggedMod = mod;
            _dragStartPoint = e.GetPosition(this);
            e.Handled = true;
        }
    }

    private void DragHandle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedMod == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _draggedMod;
        _draggedMod = null;
        var data = new WpfDataObject(typeof(ModInfo), source);
        DragDrop.DoDragDrop((DependencyObject)sender, data, WpfDragDropEffects.Move);
        e.Handled = true;
    }

    private void ModCard_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ModInfo))
            ? WpfDragDropEffects.Move
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void ModCard_Drop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData(typeof(ModInfo)) is not ModInfo source
            || (sender as FrameworkElement)?.DataContext is not ModInfo target)
            return;

        (DataContext as ModsViewModel)?.MoveMod(source, target);
        e.Handled = true;
    }

    private void TabHelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        TabHelp.Show((sender as System.Windows.Controls.Button)?.Tag as string);
    }}
