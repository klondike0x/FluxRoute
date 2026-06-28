using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

public partial class CustomDialog : Window
{
    public bool DialogConfirmed { get; private set; }

    public CustomDialog()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogConfirmed = false;
                Close();
            }
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
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
    /// Shows a styled modal dialog centered on the active window.
    /// </summary>
    public static bool Show(
        string title,
        string message,
        string confirmText = "Да",
        string cancelText = "Отмена",
        bool isDanger = false)
    {
        var dialog = new CustomDialog();
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmBtn.Content = confirmText;
        dialog.CancelBtn.Content = cancelText;
        dialog.ConfirmBtn.Style = (Style)dialog.FindResource(
            isDanger ? "DangerConfirmBtn" : "AccentConfirmBtn");
        dialog.CancelBtn.Visibility = string.IsNullOrEmpty(cancelText)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog.DialogConfirmed;
    }
}
