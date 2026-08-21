using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class AdaptiveSidebarLayoutTests
{
    [Fact]
    public void ResizeDebounce_IsLongEnoughToCoalesceLiveResizeEvents()
    {
        Assert.InRange(AdaptiveSidebarLayout.ResizeDebounceMilliseconds, 80, 150);
    }

    [Theory]
    [InlineData(1199.99, false)]
    [InlineData(1200, true)]
    [InlineData(1440, true)]
    public void ShouldExpand_UsesDesktopBreakpoint(double width, bool expected)
    {
        Assert.Equal(expected, AdaptiveSidebarLayout.ShouldExpand(width));
    }

    [Fact]
    public void RequiresVisualSync_ViewModelExpandedButViewCollapsed_ReturnsTrue()
    {
        Assert.True(AdaptiveSidebarLayout.RequiresVisualSync(
            shouldExpand: true,
            viewIsExpanded: false,
            currentColumnWidth: 66));
    }

    [Fact]
    public void RequiresVisualSync_ExpandedViewAtTargetWidth_ReturnsFalse()
    {
        Assert.False(AdaptiveSidebarLayout.RequiresVisualSync(
            shouldExpand: true,
            viewIsExpanded: true,
            currentColumnWidth: AdaptiveSidebarLayout.ExpandedWidth));
    }

    [Fact]
    public void ShouldStartAnimation_SameTargetAlreadyAnimating_ReturnsFalse()
    {
        Assert.False(AdaptiveSidebarLayout.ShouldStartAnimation(
            animationIsRunning: true,
            activeTargetWidth: AdaptiveSidebarLayout.ExpandedWidth,
            requestedTargetWidth: AdaptiveSidebarLayout.ExpandedWidth));
    }

    [Fact]
    public void ShouldStartAnimation_OppositeTargetRequested_ReturnsTrue()
    {
        Assert.True(AdaptiveSidebarLayout.ShouldStartAnimation(
            animationIsRunning: true,
            activeTargetWidth: AdaptiveSidebarLayout.ExpandedWidth,
            requestedTargetWidth: AdaptiveSidebarLayout.CollapsedWidth));
    }

    [Theory]
    [InlineData(0, 66)]
    [InlineData(0.5, 197.25)]
    [InlineData(1, 241)]
    public void InterpolateWidth_UsesClampedEaseOut(double progress, double expected)
    {
        Assert.Equal(expected, AdaptiveSidebarLayout.InterpolateWidth(66, 241, progress), 2);
    }
}
