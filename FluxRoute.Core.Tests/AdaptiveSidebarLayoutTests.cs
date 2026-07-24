using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class AdaptiveSidebarLayoutTests
{
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
}
