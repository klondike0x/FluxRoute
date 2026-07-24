namespace FluxRoute.ViewModels;

public static class AdaptiveSidebarLayout
{
    public const double ExpandedBreakpoint = 1200;
    public const double CollapsedWidth = 66;
    public const double ExpandedWidth = 241;
    public const int ResizeDebounceMilliseconds = 100;

    public static bool ShouldExpand(double windowWidth) =>
        double.IsFinite(windowWidth) && windowWidth >= ExpandedBreakpoint;

    public static bool RequiresVisualSync(
        bool shouldExpand,
        bool viewIsExpanded,
        double currentColumnWidth)
    {
        var expectedWidth = shouldExpand ? ExpandedWidth : CollapsedWidth;

        return viewIsExpanded != shouldExpand
            || !double.IsFinite(currentColumnWidth)
            || Math.Abs(currentColumnWidth - expectedWidth) > 0.5;
    }

    public static bool ShouldStartAnimation(
        bool animationIsRunning,
        double activeTargetWidth,
        double requestedTargetWidth) =>
        !animationIsRunning
        || !double.IsFinite(activeTargetWidth)
        || Math.Abs(activeTargetWidth - requestedTargetWidth) > 0.5;

    public static double InterpolateWidth(double from, double to, double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);
        var easedProgress = 1 - Math.Pow(1 - clampedProgress, 2);
        return from + (to - from) * easedProgress;
    }
}
