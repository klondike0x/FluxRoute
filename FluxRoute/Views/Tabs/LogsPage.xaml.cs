using System.Windows.Controls;
using FluxRoute.ViewModels;
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

    /// <summary>
    /// Автопрокрутка при изменении текста, если включён чекбокс.
    /// v1.7.0: UI-Redesign
    /// </summary>
    private void UnifiedLogsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is LogsViewModel vm && vm.LogsAutoScroll)
            ScrollToEnd();
    }
}