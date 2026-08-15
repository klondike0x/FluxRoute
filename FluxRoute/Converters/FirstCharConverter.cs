using System.Globalization;
using System.Windows.Data;

namespace FluxRoute.Converters;

/// <summary>
/// Возвращает первую букву строки в верхнем регистре. Если строка пуста — «?».
/// </summary>
public sealed class FirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s))
            return "?";
        return s.TrimStart()[0].ToString().ToUpper();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
