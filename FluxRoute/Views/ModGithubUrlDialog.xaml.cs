using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

/// <summary>
/// Диалог ввода GitHub-репозитория для синхронизации модов.
/// </summary>
public partial class ModGithubUrlDialog : Window
{
    /// <summary>Введённый URL репозитория.</summary>
    public string RepoUrl => UrlBox.Text.Trim();

    /// <summary>Подтверждено ли действие.</summary>
    public bool DialogConfirmed { get; private set; }

    public ModGithubUrlDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UrlBox.SelectAll();
            Dispatcher.BeginInvoke(new Action(() => UrlBox.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    private void UrlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Confirm_Click(sender, e);
        else if (e.Key == Key.Escape)
            Cancel_Click(sender, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
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
    /// Показывает диалог ввода GitHub-URL.
    /// </summary>
    public static string? ShowGithubUrlDialog()
    {
        var dialog = new ModGithubUrlDialog();
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog.DialogConfirmed ? dialog.RepoUrl : null;
    }
}
