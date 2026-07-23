using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

/// <summary>
/// Модальное окно проверки и результата обновления компонента.
/// </summary>
public partial class UpdateCheckDialog : Window
{
    private bool _primaryResult;
    private readonly TaskCompletionSource<bool> _resultSource = new();
    private Window? _modalOwner;

    public UpdateCheckDialog(string componentName, string currentVersion)
    {
        InitializeComponent();
        ComponentText.Text = componentName;
        CurrentVersionText.Text = DisplayVersion(currentVersion);
        MessageText.Text = "Получаем информацию о последней доступной версии…";
        LatestVersionText.Text = "…";

        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
        if (owner is { IsLoaded: true })
        {
            Owner = owner;
            _modalOwner = owner;
        }
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Closed += (_, _) =>
        {
            if (_modalOwner is not null)
            {
                _modalOwner.IsEnabled = true;
                _modalOwner.Activate();
            }
            _resultSource.TrySetResult(_primaryResult);
        };
    }

    public void ShowChecking(string message = "Проверяем наличие обновлений…")
    {
        TitleText.Text = "Проверка обновлений";
        StatusIcon.Text = "↻";
        StatusIcon.Foreground = System.Windows.Media.Brushes.DeepSkyBlue;
        MessageText.Text = message;
        LatestVersionText.Text = "…";
        ProgressIndicator.Visibility = Visibility.Visible;
        ButtonsPanel.Visibility = Visibility.Collapsed;
    }

    public void ShowUpToDate(string currentVersion, string latestVersion)
    {
        TitleText.Text = "Установлена актуальная версия";
        StatusIcon.Text = "✓";
        StatusIcon.Foreground = System.Windows.Media.Brushes.MediumSpringGreen;
        MessageText.Text = "Обновления не требуются — компонент уже обновлён до последней версии.";
        SetVersions(currentVersion, latestVersion);
        ShowResultButtons(primaryText: "Закрыть", secondaryText: string.Empty);
    }

    public void ShowUpdateAvailable(string currentVersion, string latestVersion, bool autoInstall)
    {
        TitleText.Text = autoInstall ? "Найдена новая версия" : "Обновление доступно";
        StatusIcon.Text = "↓";
        StatusIcon.Foreground = System.Windows.Media.Brushes.DeepSkyBlue;
        MessageText.Text = autoInstall
            ? "Новая версия найдена. Начинаем автоматическое обновление…"
            : "Доступна новая версия компонента. Установить обновление сейчас?";
        SetVersions(currentVersion, latestVersion);
        if (autoInstall)
            ShowInstalling("Загрузка и установка обновления…");
        else
            ShowResultButtons(primaryText: "Обновить", secondaryText: "Позже");
    }

    public void ShowInstalling(string message)
    {
        TitleText.Text = "Установка обновления";
        StatusIcon.Text = "↓";
        MessageText.Text = message;
        ProgressIndicator.Visibility = Visibility.Visible;
        ButtonsPanel.Visibility = Visibility.Collapsed;
    }

    public void ShowInstalled(string currentVersion, string latestVersion)
    {
        TitleText.Text = "Обновление установлено";
        StatusIcon.Text = "✓";
        StatusIcon.Foreground = System.Windows.Media.Brushes.MediumSpringGreen;
        MessageText.Text = $"Компонент успешно обновлён до версии {DisplayVersion(latestVersion)}.";
        SetVersions(currentVersion, latestVersion);
        ShowResultButtons(primaryText: "Закрыть", secondaryText: string.Empty);
    }

    public void ShowError(string message, string currentVersion, string? latestVersion = null)
    {
        TitleText.Text = "Не удалось проверить обновления";
        StatusIcon.Text = "!";
        StatusIcon.Foreground = System.Windows.Media.Brushes.OrangeRed;
        MessageText.Text = message;
        SetVersions(currentVersion, latestVersion);
        ShowResultButtons(primaryText: "Закрыть", secondaryText: string.Empty);
    }

    public void OpenModal()
    {
        if (_modalOwner is not null)
            _modalOwner.IsEnabled = false;
        Show();
    }

    public Task<bool> WaitForResultAsync() => _resultSource.Task;

    private void SetVersions(string? currentVersion, string? latestVersion)
    {
        CurrentVersionText.Text = DisplayVersion(currentVersion);
        LatestVersionText.Text = DisplayVersion(latestVersion);
    }

    private void ShowResultButtons(string primaryText, string secondaryText)
    {
        ProgressIndicator.Visibility = Visibility.Collapsed;
        ButtonsPanel.Visibility = Visibility.Visible;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
        SecondaryButton.Visibility = string.IsNullOrWhiteSpace(secondaryText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string DisplayVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "—" : version;

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        _primaryResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        _primaryResult = false;
        Close();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
