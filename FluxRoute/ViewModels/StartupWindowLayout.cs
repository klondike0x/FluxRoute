using FluxRoute.Core.Models;

namespace FluxRoute.ViewModels;

public readonly record struct StartupWindowSize(double Width, double Height);

public static class StartupWindowLayout
{
    public const double ModernWidth = 1440;
    public const double ModernHeight = 826;
    public const double MinimalWidth = 860;
    public const double MinimalHeight = 520;

    public static StartupWindowSize GetRequestedSize(StartupWindowMode mode) => mode switch
    {
        StartupWindowMode.Modern => new StartupWindowSize(ModernWidth, ModernHeight),
        _ => new StartupWindowSize(MinimalWidth, MinimalHeight)
    };

    public static StartupWindowSize FitToWorkArea(
        StartupWindowMode mode,
        double workAreaWidth,
        double workAreaHeight)
    {
        var requested = GetRequestedSize(mode);
        var availableWidth = double.IsFinite(workAreaWidth)
            ? Math.Max(MinimalWidth, workAreaWidth)
            : MinimalWidth;
        var availableHeight = double.IsFinite(workAreaHeight)
            ? Math.Max(MinimalHeight, workAreaHeight)
            : MinimalHeight;

        return new StartupWindowSize(
            Math.Min(requested.Width, availableWidth),
            Math.Min(requested.Height, availableHeight));
    }
}
