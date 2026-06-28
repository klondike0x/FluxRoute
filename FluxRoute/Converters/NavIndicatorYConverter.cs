using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace FluxRoute.Converters;

public sealed class NavIndicatorYConverter : IValueConverter
{
    private static readonly double[] TabY =
    [
          0,     // 0  Главная
         44,     // 1  TG Прокси
         88,     // 2  Оркестратор
        132,     // 3  ИИ
        176,     // 4  Обновление
        220,     // 5  Диагностика
        264,     // 6  Сервис
        417,     // 7  О программе — подобрать вручную
        308,     // 8  Логи
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int tabIndex = value is int i ? i : 0;
        double y = tabIndex >= 0 && tabIndex < TabY.Length ? TabY[tabIndex] : tabIndex * 44.0;
        Debug.WriteLine($"NavIndicator: tab={tabIndex}, y={y}");
        return y;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
