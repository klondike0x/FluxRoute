// ═══ v1.7.0: Tests Flowseal + Zapret2 ═══
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace FluxRoute.Core.Tests;

public class FlowsealTests
{
    [Fact] public void GetCurrentVersion_NoFile_Null() => Assert.Null(new FlowsealVersionManager(null!, null!).GetCurrentVersion("C:\\nx"));
    [Fact] public async Task Install_Empty_Fails() { var r = await new FlowsealVersionManager(null!, null!).InstallVersionAsync("", "C:\\nx"); Assert.False(r.Success); }
    [Fact] public async Task Rollback_Fails() { var r = await new FlowsealVersionManager(null!, null!).RollbackAsync("C:\\x"); Assert.False(r.Success); }
    [Fact] public void Models_Props() { var v = new FlowsealVersion("1.0"); Assert.Equal("1.0", v.Version); var r = new FlowsealInstallResult(true, "1"); Assert.True(r.Success); }
}

public class Zapret2Tests
{
    private readonly Zapret2CompatibilityService _svc = new();
    [Fact] public void NotNull() => Assert.NotNull(_svc.CheckCapability());
    [Fact] public void HasArch() => Assert.Contains("64", _svc.CheckCapability().OsArchitecture!);
    [Fact] public async Task SyncAsync_Match() { var s = _svc.CheckCapability(); var a = await _svc.CheckCapabilityAsync(); Assert.Equal(s.CanRun, a.CanRun); }
    [Fact] public void Capability_Props() { var c = new Zapret2Capability(true, true, "1.0", true, "X64", "10.0"); Assert.True(c.CanRun); Assert.Equal("1.0", c.WinDivertVersion); }
}
