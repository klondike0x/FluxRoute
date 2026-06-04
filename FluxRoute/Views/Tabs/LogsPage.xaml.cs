using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;

namespace FluxRoute.Views.Tabs;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();
    }

    /// <summary>Прокрутить лог-поле в конец.</summary>
    public void ScrollToEnd()
    {
        if (UnifiedLogsTextBox is { } tb)
        {
            tb.CaretIndex = tb.Text.Length;
            tb.ScrollToEnd();
        }
    }
}