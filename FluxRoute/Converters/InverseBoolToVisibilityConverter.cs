using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FluxRoute.Converters;

/// <summary>
/// Возвращает Collapsed когда true, Visible когда false.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
            return v != Visibility.Visible;
        return false;
    }
}
