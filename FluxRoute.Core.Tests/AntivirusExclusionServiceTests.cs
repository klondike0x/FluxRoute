// ═══ v1.7.0: Тесты AntivirusExclusionService ═══
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public class AntivirusExclusionServiceTests
{
    private readonly AntivirusExclusionService _svc = new(new Mock<ILogger<AntivirusExclusionService>>().Object);

    [Fact] public async Task EmptyPath_Fails() { var r = await _svc.AddExclusionAsync(""); Assert.False(r.Success); }
    [Fact] public async Task WhitespacePath_Fails() { var r = await _svc.AddExclusionAsync("   "); Assert.False(r.Success); }
    [Fact] public async Task NonexistentDir_Fails() { var r = await _svc.AddExclusionAsync(Path.Combine(Path.GetTempPath(), $"nx_{Guid.NewGuid():N}")); Assert.False(r.Success); }
    [Fact] public async Task IsExcluded_Empty_False() => Assert.False(await _svc.IsExcludedAsync(""));
    [Fact] public async Task IsExcluded_Nonexistent_False() => Assert.False(await _svc.IsExcludedAsync(@"C:\nx_12345"));
    [Fact] public void Result_Props() { var r = new AntivirusExclusionResult { Path = "P", Success = true, Message = "M", RequiresElevation = false }; Assert.Equal("P", r.Path); Assert.True(r.Success); }
}
