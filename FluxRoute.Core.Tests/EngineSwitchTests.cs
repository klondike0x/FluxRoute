// ═══ v1.7.0: Tests EngineSwitch + AutoStrategy ═══
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public class EngineSwitchTests
{
    private readonly EngineSwitchService _svc = new(new Mock<ILogger<EngineSwitchService>>().Object);
    [Fact] public async Task CanSwitch_Zapret1_True() => Assert.True(await _svc.CanSwitchToAsync("Zapret"));
    [Fact] public async Task Test_EmptyPath_False() => Assert.False(await _svc.TestStrategyAsync("", new[] { "x.com" }));
    [Fact] public async Task Test_EmptySites_True() => Assert.True(await _svc.TestStrategyAsync("C:\\x.bat", Array.Empty<string>()));
}

public class AutoStrategyTests
{
    [Fact] public async Task EmptyProfiles_Fails() { var s = new AutoStrategyService(null!, null!); var r = await s.AutoPickAsync(Array.Empty<ProfileItem>(), new[] { "x.com" }); Assert.False(r.Success); }
}
