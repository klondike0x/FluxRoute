using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

/// <summary>
/// Диалог ввода названия нового мода.
/// </summary>
public partial class ModNameDialog : Window
{
    /// <summary>Введённое название мода.</summary>
    public string ModName => NameBox.Text.Trim();

    /// <summary>Подтверждено ли создание.</summary>
    public bool DialogConfirmed { get; private set; }

    public ModNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => NameBox.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
    }

    private void NameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Confirm_Click(sender, e);
        else if (e.Key == Key.Escape)
            Cancel_Click(sender, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return;
        DialogConfirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogConfirmed = false;
        Close();
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>
    /// Показывает диалог создания мода поверх активного окна.
    /// Возвращает введённое имя или null при отмене.
    /// </summary>
    public static string? ShowCreateDialog()
    {
        var dialog = new ModNameDialog();
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog.DialogConfirmed ? dialog.ModName : null;
    }
}
