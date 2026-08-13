using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class ProtectionModeTests
{
    [Fact]
    public void FromOrchestratorEnabled_True_ReturnsAutomatic()
        => Assert.Equal(ProtectionMode.Automatic, ProtectionModePolicy.FromOrchestratorEnabled(true));

    [Fact]
    public void FromOrchestratorEnabled_False_ReturnsManual()
        => Assert.Equal(ProtectionMode.Manual, ProtectionModePolicy.FromOrchestratorEnabled(false));

    [Theory]
    [InlineData(ProtectionMode.Automatic, "Автовыбор")]
    [InlineData(ProtectionMode.Manual, "Вручную")]
    public void GetDisplayName_KnownMode_ReturnsRussianName(ProtectionMode mode, string expected)
        => Assert.Equal(expected, ProtectionModePolicy.GetDisplayName(mode));
}
