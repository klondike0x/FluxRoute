using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

public sealed class DohSelectionServiceTests
{
    [Fact]
    public void SelectBest_WhenProvidersAreAvailable_PrefersStableLowLatencyProvider()
    {
        // Arrange
        var results = new[]
        {
            CreateResult("fast-unstable", 45, 0.50),
            CreateResult("balanced", 55, 1.00),
            CreateResult("slow", 130, 1.00)
        };
        var service = new DohSelectionService();

        // Act
        var selected = service.SelectBest(results);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal("balanced", selected.Provider.Id);
    }

    [Fact]
    public void SelectBest_WhenAllProvidersFailed_ReturnsNull()
    {
        // Arrange
        var results = new[]
        {
            CreateResult("first", null, 0),
            CreateResult("second", null, 0)
        };
        var service = new DohSelectionService();

        // Act
        var selected = service.SelectBest(results);

        // Assert
        Assert.Null(selected);
    }

    [Fact]
    public void Rank_ReturnsSuccessfulProvidersBeforeFailedProviders()
    {
        // Arrange
        var results = new[]
        {
            CreateResult("failed", null, 0),
            CreateResult("working", 90, 1.00)
        };
        var service = new DohSelectionService();

        // Act
        var ranked = service.Rank(results);

        // Assert
        Assert.Equal("working", ranked[0].Provider.Id);
        Assert.Equal("failed", ranked[1].Provider.Id);
    }

    private static DohProviderTestResult CreateResult(string id, double? latency, double successRate)
    {
        var provider = new DohProvider(
            id,
            id,
            new Uri($"https://{id}.example/dns-query"),
            ["1.1.1.1"],
            false);

        return new DohProviderTestResult(provider, latency, successRate, 4, null, 0);
    }
}
