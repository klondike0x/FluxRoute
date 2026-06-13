using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FluxRoute.Converters;

public sealed class LogColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(System.Windows.Media.Color.FromRgb(63, 185, 80));   // #3FB950
    private static readonly SolidColorBrush RedBrush = new(System.Windows.Media.Color.FromRgb(248, 81, 73));     // #F85149
    private static readonly SolidColorBrush GoldBrush = new(System.Windows.Media.Color.FromRgb(255, 215, 0));    // #FFD700
    private static readonly SolidColorBrush BlueBrush = new(System.Windows.Media.Color.FromRgb(79, 195, 247));   // #4FC3F7
    private static readonly SolidColorBrush DefaultBrush = new(System.Windows.Media.Color.FromRgb(139, 148, 158)); // #8B949E

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";

        // Success / Positive
        if (text.Contains("✅")) return GreenBrush;

        // Error / Destructive
        if (text.Contains("❌") || text.Contains("⏹")) return RedBrush;

        // Warning
        if (text.Contains("⚠️") || text.Contains("⚠")) return GoldBrush;

        // Actions / Progress / Stats
        if (text.Contains("🔄") || text.Contains("🚀") || text.Contains("📊") ||
            text.Contains("▶") || text.Contains("📋") || text.Contains("📦") ||
            text.Contains("⬇️") || text.Contains("⏳") || text.Contains("🔍") ||
            text.Contains("📲")) return BlueBrush;

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
