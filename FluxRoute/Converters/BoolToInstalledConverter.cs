using System.Globalization;
using System.Windows.Data;

namespace FluxRoute.Converters;

[ValueConversion(typeof(bool), typeof(string))]
public sealed class BoolToInstalledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "✅ Да" : "❌ Нет";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
