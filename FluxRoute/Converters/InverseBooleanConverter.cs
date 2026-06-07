using System;
using System.Globalization;
using System.Windows.Data;

namespace FluxRoute.Converters;

/// <summary>
/// Converts true to false and false to true for XAML bindings.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool boolValue ? !boolValue : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool boolValue ? !boolValue : value;
}