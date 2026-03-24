using System.Diagnostics;
using System.Windows;
using Application = System.Windows.Application;

namespace FluxRoute.Views;

public partial class AdminPromptWindow : Window
{
    /// <summary>
    /// true — пользователь выбрал «Продолжить без прав»,
    /// false (или закрыл окно) — приложение не запускается.
    /// При перезапуске окно закрывается и процесс перезапускается от имени администратора.
    /// </summary>
    public bool ContinueWithoutAdmin { get; private set; }

    public AdminPromptWindow()
    {
        InitializeComponent();
    }

    private void RestartAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is not null)
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
        }
        catch
        {
            // Пользователь отменил UAC — остаёмся
            return;
        }

        // Завершаем текущий процесс без прав
        System.Windows.Application.Current.Shutdown();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        ContinueWithoutAdmin = true;
        Close();
    }
}
