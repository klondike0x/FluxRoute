namespace FluxRoute.ViewModels;

public static class AdaptiveSidebarLayout
{
    public const double ExpandedBreakpoint = 1200;
    public const double CollapsedWidth = 66;
    public const double ExpandedWidth = 241;

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
}
