using System.Windows;
using System.Windows.Controls;
using FluxRoute.Views;

namespace FluxRoute.Views.Tabs;

public partial class ServicePage : System.Windows.Controls.UserControl
{
    public ServicePage()
    {
        InitializeComponent();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var topic = (sender as System.Windows.Controls.Button)?.Tag as string;
        var (title, message) = topic switch
        {
            "GameFilter" => (
                "Game Filter",
                "Game Filter расширяет диапазон портов, которые обрабатывает защита. Это может помочь игровым соединениям обходить ограничения.\n\nTCP и UDP — проверять оба типа трафика. Отдельный протокол — проверять только выбранный тип."),
            "IpSet" => (
                "IPSet Filter",
                "IPSet определяет, какие IP-адреса обрабатывает защита.\n\nloaded — использовать загруженный список IP; none — не использовать список; any — обрабатывать любые адреса. Обычно рекомендуется loaded."),
            "Recovery" => (
                "Восстановление защиты",
                "Обычное восстановление перезапускает текущую стратегию.\n\nПолное восстановление дополнительно останавливает и очищает службу zapret. Освобождение WinDivert нужно, когда драйвер занят и его невозможно заменить или удалить."),
            _ => ("Справка", "Для этого элемента пока нет дополнительного описания.")
        };

        CustomDialog.Show(title, message, "Понятно", "");
    }
}