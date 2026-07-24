namespace FluxRoute.ViewModels;

public enum HomeLayoutMode
{
    Compact,
    Wide
}

public readonly record struct HomeLayoutSpec(
    bool ShowCompactSummaryCards,
    bool ShowWideDetails,
    bool ShowWideMonitor,
    int CompactSummaryColumnCount,
    double DetailsWidth,
    double DetailsGap);

public static class AdaptiveHomeLayout
{
    public const double MinimumWindowWidth = 860;
    public const double MinimumWindowHeight = 520;
    public const double WideBreakpoint = 1100;

    public static HomeLayoutMode FromWindowWidth(double width) =>
        double.IsFinite(width) && width >= WideBreakpoint
            ? HomeLayoutMode.Wide
            : HomeLayoutMode.Compact;

    public static HomeLayoutSpec GetSpec(HomeLayoutMode mode) => mode switch
    {
        HomeLayoutMode.Wide => new HomeLayoutSpec(
            ShowCompactSummaryCards: false,
            ShowWideDetails: true,
            ShowWideMonitor: true,
            CompactSummaryColumnCount: 0,
            DetailsWidth: 280,
            DetailsGap: 24),
        _ => new HomeLayoutSpec(
            ShowCompactSummaryCards: true,
            ShowWideDetails: false,
            ShowWideMonitor: false,
            CompactSummaryColumnCount: 3,
            DetailsWidth: 0,
            DetailsGap: 0)
    };
}
