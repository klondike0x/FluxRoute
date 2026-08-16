using System.Windows;
using System.Windows.Controls;
using FluxRoute.Views;

namespace FluxRoute.Views.Tabs;

public partial class TgProxyPage : System.Windows.Controls.UserControl
{
    public TgProxyPage()
    {
        InitializeComponent();
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var topic = (sender as System.Windows.Controls.Button)?.Tag as string;
        var (title, message) = topic switch
        {
            "MtProto" => (
                "Подключение MTProto",
                "Укажите адрес и порт MTProto-сервера, а также secret. Эти параметры используются встроенным прокси для подключения к Telegram."),
            "DataCenters" => (
                "Датацентры Telegram",
                "Необязательная таблица соответствий номера датацентра Telegram и его IP-адреса.\n\nФормат: один адрес на строку, например 2:149.154.167.50."),
            "CloudflareProxy" => (
                "Cloudflare Proxy",
                "Дополнительный маршрут через Cloudflare. Его можно включить, если прямое подключение к Telegram работает нестабильно."),
            "CloudflareWorker" => (
                "Cloudflare Worker",
                "Домены Worker используются как дополнительные WebSocket-маршруты и проверяются перед прямым подключением."),
            "Performance" => (
                "Логи и производительность",
                "Verbose включает подробное логирование. Буфер влияет на размер сетевого буфера, пул — на число параллельных WebSocket-сессий, а максимальный размер лога ограничивает его хранение.\n\nИзменения применяются после остановки и повторного запуска прокси."),
            "Pool" => (
                "Пул WebSocket-сессий",
                "Пул определяет, сколько WebSocket-сессий прокси может обслуживать параллельно. Обычно оставляйте стандартное значение 4: слишком большое значение увеличивает нагрузку на систему."),
            _ => ("Справка", "Для этого элемента пока нет дополнительного описания.")
        };

        CustomDialog.Show(title, message, "Понятно", "");
    }
}