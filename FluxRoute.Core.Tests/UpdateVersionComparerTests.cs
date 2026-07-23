using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class UpdateVersionComparerTests
{
    [Theory]
    [InlineData("v1.8.1", "1.8.1")]
    [InlineData("1.8.1", "v1.8.1")]
    [InlineData(" V2.0.0 ", "2.0.0")]
    public void AreEqual_NormalizedVersions_ReturnsTrue(string local, string latest)
        => Assert.True(UpdateVersionComparer.AreEqual(local, latest));

    [Theory]
    [InlineData("1.8.0", "1.8.1")]
    [InlineData("—", "1.8.1")]
    [InlineData("", "1.8.1")]
    public void AreEqual_DifferentOrMissingVersions_ReturnsFalse(string local, string latest)
        => Assert.False(UpdateVersionComparer.AreEqual(local, latest));
}
