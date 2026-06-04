namespace FluxRoute.Views.Tabs;

public partial class LogsPage : System.Windows.Controls.UserControl
{
    public LogsPage()
    {
        InitializeComponent();
    }

    /// <summary>Прокрутить лог-поле в конец.</summary>
    public void ScrollToEnd()
    {
        UnifiedLogsTextBox.CaretIndex = UnifiedLogsTextBox.Text.Length;
        UnifiedLogsTextBox.ScrollToEnd();
    }
}
