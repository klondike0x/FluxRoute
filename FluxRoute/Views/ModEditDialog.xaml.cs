using System.Windows;
using System.Windows.Input;
using FluxRoute.Core.Models;
using Application = System.Windows.Application;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FluxRoute.Views;

public sealed record ModEditValues(string Name, string Version, string Author, string Description);

/// <summary>Тёмное модальное окно редактирования метаданных мода.</summary>
public partial class ModEditDialog : Window
{
    public ModEditValues? Values { get; private set; }

    public ModEditDialog(ModInfo mod)
    {
        InitializeComponent();
        NameBox.Text = mod.Name;
        VersionBox.Text = mod.Version;
        AuthorBox.Text = mod.Author;
        DescriptionBox.Text = mod.Description;
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => NameBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        Values = new ModEditValues(
            name,
            VersionBox.Text.Trim(),
            AuthorBox.Text.Trim(),
            DescriptionBox.Text.Trim());
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public static ModEditValues? ShowEditDialog(ModInfo mod)
    {
        var dialog = new ModEditDialog(mod);
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog.Values;
    }
}
