using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Контракт новой навигации FluxRoute 1.7.0.
/// </summary>
public sealed class MainNavigationTests
{
    [Theory]
    [InlineData(0, "ГЛАВНАЯ")]
    [InlineData(1, "КОМПОНЕНТЫ")]
    [InlineData(2, "ОРКЕСТРАТОР")]
    [InlineData(3, "ХОСТЛИСТЫ")]
    [InlineData(4, "МОДИФИКАЦИИ")]
    [InlineData(5, "ДИАГНОСТИКА")]
    [InlineData(6, "ЛОГИ")]
    [InlineData(7, "НАСТРОЙКИ")]
    public void GetName_KnownIndex_ReturnsExpectedName(int index, string expected)
        => Assert.Equal(expected, MainNavigation.GetName(index));

    [Fact]
    public void GetName_UnknownIndex_ReturnsEmptyString()
        => Assert.Equal(string.Empty, MainNavigation.GetName(99));

    [Fact]
    public void Items_ContainsExactlyEightOrderedTabs()
    {
        Assert.Equal(8, MainNavigation.Items.Count);
        Assert.Equal(Enumerable.Range(0, 8), MainNavigation.Items.Select(item => item.Index));
    }
}
