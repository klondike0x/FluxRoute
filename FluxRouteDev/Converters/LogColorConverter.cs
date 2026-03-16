using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FluxRoute.Converters;

public sealed class LogColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "";

        if (text.Contains("✅")) return new SolidColorBrush(Color.FromRgb(34, 139, 34));
        if (text.Contains("❌")) return new SolidColorBrush(Color.FromRgb(200, 40, 40));
        if (text.Contains("⚠️")) return new SolidColorBrush(Color.FromRgb(200, 140, 0));
        if (text.Contains("🔄")) return new SolidColorBrush(Color.FromRgb(30, 100, 200));
        if (text.Contains("🚀")) return new SolidColorBrush(Color.FromRgb(30, 100, 200));
        if (text.Contains("📊")) return new SolidColorBrush(Color.FromRgb(100, 60, 180));

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}