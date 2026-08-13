// ═══ v1.7.0: Tests Mods ═══
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public class ModsTests
{
    [Fact] public void MtProto_GeneratesSecret() { var s = new MtProtoProxyService(new Mock<ILogger<MtProtoProxyService>>().Object); Assert.NotEmpty(s.Config.Secret); Assert.Equal(32, s.Config.Secret.Length); }
    [Fact] public async Task MtProto_StartStop() { var s = new MtProtoProxyService(new Mock<ILogger<MtProtoProxyService>>().Object); await s.StartAsync(); Assert.Equal(MtProtoState.Running, s.State); await s.StopAsync(); Assert.Equal(MtProtoState.Stopped, s.State); }
    [Fact] public async Task Editor_ReadWrite() { var e = new StrategyEditorService(new Mock<ILogger<StrategyEditorService>>().Object); var tmp = Path.GetTempFileName(); try { await e.WriteFileAsync(tmp, "test"); Assert.Equal("test", await e.ReadFileAsync(tmp)); } finally { File.Delete(tmp); } }
    [Fact] public void Editor_FindFiles_Empty() { var e = new StrategyEditorService(new Mock<ILogger<StrategyEditorService>>().Object); Assert.Empty(e.FindFiles("C:\\nonexistent_dir", "*.bat")); }
    [Fact] public void SiteDiagnostic_Props() { var d = new FluxRoute.Core.Models.SiteDiagnosticResult { Site = "google.com", IsAccessible = true, LatencyMs = 42 }; Assert.True(d.IsAccessible); Assert.Equal(42L, d.LatencyMs); }
}
