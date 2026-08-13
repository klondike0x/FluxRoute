using FluxRoute.Services;

namespace FluxRoute.Core.Tests;

public sealed class TrayPopupPlacementTests
{
    [Fact]
    public void IsPreviewRequested_WithPreviewArgument_ReturnsTrue()
    {
        Assert.True(TrayPopupService.IsPreviewRequested(["FluxRoute.exe", "--tray-preview"]));
    }

    [Fact]
    public void IsPreviewRequested_WithoutPreviewArgument_ReturnsFalse()
    {
        Assert.False(TrayPopupService.IsPreviewRequested(["FluxRoute.exe"]));
    }

    [Fact]
    public void Calculate_DefaultTrayPosition_PlacesPopupAboveCursor()
    {
        // Act
        var bounds = TrayPopupPlacement.Calculate(
            cursorX: 1900,
            cursorY: 1040,
            popupWidth: 292,
            popupHeight: 350,
            workLeft: 0,
            workTop: 0,
            workRight: 1920,
            workBottom: 1040);

        // Assert
        Assert.Equal(1608, bounds.Left);
        Assert.Equal(682, bounds.Top);
    }

    [Fact]
    public void Calculate_NearTopLeft_ClampsToWorkAreaMargin()
    {
        // Act
        var bounds = TrayPopupPlacement.Calculate(
            cursorX: 2,
            cursorY: 2,
            popupWidth: 292,
            popupHeight: 350,
            workLeft: 0,
            workTop: 0,
            workRight: 1920,
            workBottom: 1040);

        // Assert
        Assert.Equal(6, bounds.Left);
        Assert.Equal(6, bounds.Top);
    }

    [Fact]
    public void Calculate_SecondaryMonitorWithNegativeCoordinates_StaysInsideMonitor()
    {
        // Act
        var bounds = TrayPopupPlacement.Calculate(
            cursorX: -10,
            cursorY: 900,
            popupWidth: 292,
            popupHeight: 350,
            workLeft: -1920,
            workTop: 0,
            workRight: 0,
            workBottom: 1040);

        // Assert
        Assert.True(bounds.Left >= -1914);
        Assert.True(bounds.Left + bounds.Width <= -6);
        Assert.True(bounds.Top >= 6);
        Assert.True(bounds.Top + bounds.Height <= 1034);
    }
}
