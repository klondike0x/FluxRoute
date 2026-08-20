using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class OnboardingViewModelTests
{
    [Fact]
    public void ProbeTargetSelection_MapsToExpectedSites()
    {
        var viewModel = new OnboardingViewModel();

        Assert.Equal(["Zapret"], viewModel.ComponentOptions);
        Assert.Equal(["YouTube", "Discord", "YouTube + Discord"], viewModel.ProbeTargetOptions);
        Assert.Equal(2, viewModel.SelectedProbeTargetIndex);
        Assert.True(viewModel.ProbeYouTubeEnabled);
        Assert.True(viewModel.ProbeDiscordEnabled);

        viewModel.SelectedProbeTargetIndex = 0;
        Assert.True(viewModel.ProbeYouTubeEnabled);
        Assert.False(viewModel.ProbeDiscordEnabled);

        viewModel.SelectedProbeTargetIndex = 1;
        Assert.False(viewModel.ProbeYouTubeEnabled);
        Assert.True(viewModel.ProbeDiscordEnabled);
    }

    [Fact]
    public void LoadProfiles_SelectsFirstAvailableProfileAutomatically()
    {
        var directory = Directory.CreateTempSubdirectory("fluxroute-onboarding-");

        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "first.bat"), "@echo off");
            File.WriteAllText(Path.Combine(directory.FullName, "second.bat"), "@echo off");
            File.WriteAllText(Path.Combine(directory.FullName, "service.bat"), "@echo off");

            var viewModel = new OnboardingViewModel();
            viewModel.LoadProfiles(directory.FullName);

            Assert.Equal(2, viewModel.AvailableStrategies.Count);
            Assert.Equal("first.bat", viewModel.SelectedStrategyFileName);
            Assert.True(viewModel.CanComplete);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}