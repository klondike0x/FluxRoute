using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using FluxRoute.ViewModels;

namespace FluxRoute.Views;

/// <summary>
/// Источник импорта мода.
/// </summary>
public enum ModImportSource
{
    Folder,
    Zip,
    Files,
    Github
}

/// <summary>
/// Результат выбора в диалоге импорта.
/// </summary>
public sealed record ModImportResult(ModImportSource Source, string Path);

/// <summary>
/// Диалог импорта модификации в стиле Zapret-Hub:
/// выбор источника — папка / ZIP-архив / файлы / GitHub.
/// </summary>
public partial class ModImportDialog : Window
{
    private string? _selectedPath;
    private ModImportSource _selectedSource;

    public ModImportDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Скрываем панель GitHub по умолчанию
            GithubPanel.Visibility = Visibility.Collapsed;
        };
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку мода"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedPath = dlg.FolderName;
            _selectedSource = ModImportSource.Folder;
            DialogResult = true;
        }
    }

    private void Zip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите ZIP-архив мода",
            Filter = "ZIP-архивы (*.zip)|*.zip|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedPath = dlg.FileName;
            _selectedSource = ModImportSource.Zip;
            DialogResult = true;
        }
    }

    private void Files_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файлы мода",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedPath = string.Join("|", dlg.FileNames);
            _selectedSource = ModImportSource.Files;
            DialogResult = true;
        }
    }

    private void Github_Click(object sender, RoutedEventArgs e)
    {
        GithubPanel.Visibility = Visibility.Visible;
        // Фокус после отрисовки панели — иначе Storyboard в шаблоне TextBox
        // не находит RootBorder (шаблон ещё не применён к скрытому элементу)
        Dispatcher.BeginInvoke(new Action(() => GithubUrlBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void GithubUrlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            GithubConfirm_Click(sender, e);
        else if (e.Key == Key.Escape)
            Close();
    }

    private void GithubConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GithubUrlBox.Text))
            return;
        _selectedPath = GithubUrlBox.Text.Trim();
        _selectedSource = ModImportSource.Github;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>
    /// Показывает диалог импорта.
    /// </summary>
    /// <returns>Источник + путь, или null при отмене.</returns>
    public static ModImportResult? ShowImportDialog()
    {
        var dialog = new ModImportDialog();
        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;

        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var result = dialog.ShowDialog();
        if (result != true || dialog._selectedPath == null)
            return null;

        return new ModImportResult(dialog._selectedSource, dialog._selectedPath);
    }
}
