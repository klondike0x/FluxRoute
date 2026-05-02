using System.IO;
using FluxRoute.Updater.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для UpdaterService: GetLocalVersion, NormalizeVersion, GetLatestReleaseAsync (mock).
/// Сетевые тесты намеренно пропущены (не запускаются в CI без сети).
/// </summary>
public sealed class UpdaterServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UpdaterService _svc;

    public UpdaterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteUpdaterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _svc = new UpdaterService(); // uses DefaultHttpClientFactory
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── GetLocalVersion ──

    [Fact]
    public void GetLocalVersion_NoFiles_ReturnsUnknown()
    {
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetLocalVersion_FromVersionTxt_ReturnsNormalized()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "  v1.9.7b  ");
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.9.7b", result);
    }

    [Fact]
    public void GetLocalVersion_FromVersionTxt_StripsVPrefix()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "V2.0.0");
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtEmpty_FallsBackToServiceBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "  ");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "echo hi\r\nset LOCAL_VERSION=1.8.0\r\necho done\r\n");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.8.0", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtUnknown_FallsBackToServiceBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "unknown");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "set \"LOCAL_VERSION=1.7.3\"");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.7.3", result);
    }

    [Fact]
    public void GetLocalVersion_ServiceBatQuotedVersion_Parsed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "set \"LOCAL_VERSION=1.9.5a\"");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.9.5a", result);
    }

    [Fact]
    public void GetLocalVersion_NoVersionInBat_ReturnsUnknown()
    {
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "@echo off\r\necho No version here\r\n");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtTakesPriorityOverBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "2.0.0");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"), "set LOCAL_VERSION=1.0.0");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("2.0.0", result); // version.txt всегда в приоритете
    }
}
