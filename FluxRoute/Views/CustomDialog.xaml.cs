using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

public enum CustomDialogChoice
{
    Cancel,
    Confirm,
    Alternate
}

public partial class CustomDialog : Window
{
    public bool DialogConfirmed { get; private set; }
    public CustomDialogChoice Choice { get; private set; } = CustomDialogChoice.Cancel;

    public CustomDialog()
    {
        InitializeComponent();
        AlternateBtn.Visibility = Visibility.Collapsed;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Choice = CustomDialogChoice.Cancel;
                DialogConfirmed = false;
                Close();
            }
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Choice = CustomDialogChoice.Confirm;
        DialogConfirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = CustomDialogChoice.Cancel;
        DialogConfirmed = false;
        Close();
    }

    private void Alternate_Click(object sender, RoutedEventArgs e)
    {
        Choice = CustomDialogChoice.Alternate;
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

        SetOwner(dialog);
        dialog.ShowDialog();
        return dialog.DialogConfirmed;
    }

    /// <summary>
    /// Shows the FluxRoute-styled three-choice dialog for unsaved editor changes.
    /// </summary>
    public static CustomDialogChoice ShowUnsavedChanges(
        string title = "Несохранённые изменения",
        string message = "В редакторе есть несохранённые изменения. Что сделать?")
    {
        var dialog = new CustomDialog();
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmBtn.Content = "Сохранить";
        dialog.CancelBtn.Content = "Остаться";
        dialog.AlternateBtn.Content = "Не сохранять";
        dialog.AlternateBtn.Visibility = Visibility.Visible;
        dialog.ConfirmBtn.Style = (Style)dialog.FindResource("AccentConfirmBtn");
        dialog.CancelBtn.Style = (Style)dialog.FindResource("DialogCancelBtn");
        dialog.AlternateBtn.Style = (Style)dialog.FindResource("DialogCancelBtn");

        SetOwner(dialog);
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private static void SetOwner(CustomDialog dialog)
    {
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
