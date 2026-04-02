using System.Security.Principal;
using System.Windows;
using FluxRoute.Views;
using Application = System.Windows.Application;

namespace FluxRoute
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!IsRunningAsAdmin())
            {
                // Временно переключаем, чтобы закрытие диалога не завершило приложение
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var prompt = new AdminPromptWindow();
                prompt.ShowDialog();

                if (!prompt.ContinueWithoutAdmin)
                {
                    Shutdown();
                    return;
                }
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
