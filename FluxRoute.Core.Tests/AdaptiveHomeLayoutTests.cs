using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class AdaptiveHomeLayoutTests
{
    [Theory]
    [InlineData(860)]
    [InlineData(1099.99)]
    public void FromWindowWidth_BelowWideBreakpoint_ReturnsCompact(double width)
    {
        Assert.Equal(HomeLayoutMode.Compact, AdaptiveHomeLayout.FromWindowWidth(width));
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1440)]
    [InlineData(1920)]
    public void FromWindowWidth_AtOrAboveWideBreakpoint_ReturnsWide(double width)
    {
        Assert.Equal(HomeLayoutMode.Wide, AdaptiveHomeLayout.FromWindowWidth(width));
    }

    [Fact]
    public void FromWindowWidth_NonFiniteValue_ReturnsCompact()
    {
        Assert.Equal(HomeLayoutMode.Compact, AdaptiveHomeLayout.FromWindowWidth(double.NaN));
    }

    [Fact]
    public void GetSpec_Compact_ShowsHeroAndThreeSummaryCards()
    {
        var spec = AdaptiveHomeLayout.GetSpec(HomeLayoutMode.Compact);

        Assert.True(spec.ShowCompactSummaryCards);
        Assert.False(spec.ShowWideDetails);
        Assert.False(spec.ShowWideMonitor);
        Assert.Equal(3, spec.CompactSummaryColumnCount);
    }

    [Fact]
    public void GetSpec_Wide_ShowsHeroRightDetailsAndBottomMonitor()
    {
        var spec = AdaptiveHomeLayout.GetSpec(HomeLayoutMode.Wide);

        Assert.False(spec.ShowCompactSummaryCards);
        Assert.True(spec.ShowWideDetails);
        Assert.True(spec.ShowWideMonitor);
        Assert.Equal(280, spec.DetailsWidth);
        Assert.Equal(24, spec.DetailsGap);
    }
}
