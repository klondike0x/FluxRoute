using FluxRoute.Core.Services;
using FluxRoute.Core.Models;
using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class StartupWindowLayoutTests
{
    [Theory]
    [InlineData(StartupWindowMode.Modern, 1440, 826)]
    [InlineData(StartupWindowMode.Minimal, 860, 520)]
    public void GetRequestedSize_KnownMode_ReturnsExpectedDimensions(
        StartupWindowMode mode,
        double expectedWidth,
        double expectedHeight)
    {
        var size = StartupWindowLayout.GetRequestedSize(mode);

        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
    }

    [Fact]
    public void FitToWorkArea_ModernModeOnSmallDesktop_ClampsWithoutViolatingMinimum()
    {
        var size = StartupWindowLayout.FitToWorkArea(
            StartupWindowMode.Modern,
            workAreaWidth: 1280,
            workAreaHeight: 720);

        Assert.Equal(1280, size.Width);
        Assert.Equal(720, size.Height);
    }

    [Fact]
    public void AppSettings_DefaultStartupWindowMode_IsMinimal()
    {
        Assert.Equal(StartupWindowMode.Minimal, new AppSettings().StartupWindowMode);
    }

    [Fact]
    public void AppSettings_StartupWindowMode_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new SettingsService(tempDir);
            service.Save(new AppSettings { StartupWindowMode = StartupWindowMode.Modern });

            var loaded = service.Load();

            Assert.Equal(StartupWindowMode.Modern, loaded.StartupWindowMode);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
