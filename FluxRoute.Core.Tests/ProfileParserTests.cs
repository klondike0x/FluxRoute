using System.IO;
using System.Collections.ObjectModel;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для парсинга BAT-профилей (ProfileItem), TargetEntry и вспомогательной логики.
/// </summary>
public sealed class ProfileParserTests : IDisposable
{
    private readonly string _engineDir;

    public ProfileParserTests()
    {
        _engineDir = Path.Combine(Path.GetTempPath(), $"FluxRouteProfileTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_engineDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_engineDir, recursive: true); } catch { }
    }

    // ── ProfileItem ──

    [Fact]
    public void ProfileItem_DisplayName_MatchesFileNameWithoutExtension()
    {
        var p = new ProfileItem
        {
            FileName = "general (ALT3).bat",
            DisplayName = "general (ALT3)",
            FullPath = @"C:\engine\general (ALT3).bat"
        };

        Assert.Equal("general (ALT3)", p.DisplayName);
        Assert.Equal("general (ALT3).bat", p.FileName);
    }

    [Fact]
    public void ProfileItem_EmptyDefaults()
    {
        var p = new ProfileItem();
        Assert.Equal("", p.FileName);
        Assert.Equal("", p.DisplayName);
        Assert.Equal("", p.FullPath);
    }

    // ── TargetEntry.ParseFile ──

    [Fact]
    public void TargetEntry_ParseFile_NoFile_ReturnsEmpty()
    {
        var result = TargetEntry.ParseFile(Path.Combine(_engineDir, "nonexistent.txt"));
        Assert.Empty(result);
    }

    [Fact]
    public void TargetEntry_ParseFile_HttpEntries()
    {
        var path = WriteTargets("""
            YouTube=https://www.youtube.com
            Discord=https://discord.com
            """);

        var entries = TargetEntry.ParseFile(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal("YouTube", entries[0].Key);
        Assert.Equal(TargetKind.Http, entries[0].Kind);
        Assert.Equal("https://www.youtube.com", entries[0].Value);
        Assert.Equal("Discord", entries[1].Key);
    }

    [Fact]
    public void TargetEntry_ParseFile_PingEntry()
    {
        var path = WriteTargets("Google=PING:8.8.8.8");

        var entries = TargetEntry.ParseFile(path);

        Assert.Single(entries);
        Assert.Equal(TargetKind.Ping, entries[0].Kind);
        Assert.Equal("8.8.8.8", entries[0].Value);
    }

    [Fact]
    public void TargetEntry_ParseFile_SkipsComments()
    {
        var path = WriteTargets("""
            # This is a comment
            YouTube=https://youtube.com
            # Another comment
            """);

        var entries = TargetEntry.ParseFile(path);

        Assert.Single(entries);
        Assert.Equal("YouTube", entries[0].Key);
    }

    [Fact]
    public void TargetEntry_ParseFile_SkipsLinesWithoutEquals()
    {
        var path = WriteTargets("""
            NoEqualsHere
            Valid=https://example.com
            AlsoNoEquals
            """);

        var entries = TargetEntry.ParseFile(path);

        Assert.Single(entries);
        Assert.Equal("Valid", entries[0].Key);
    }

    [Fact]
    public void TargetEntry_ParseFile_StripsQuotedValues()
    {
        var path = WriteTargets("YouTube=\"https://youtube.com\"");

        var entries = TargetEntry.ParseFile(path);

        Assert.Equal("https://youtube.com", entries[0].Value);
    }

    [Fact]
    public void TargetEntry_ParseFile_MixedEntries()
    {
        var path = WriteTargets("""
            # targets
            YouTube=https://youtube.com
            PingGoogle=PING:8.8.8.8
            Discord=https://discord.com
            """);

        var entries = TargetEntry.ParseFile(path);

        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(e => e.Kind == TargetKind.Http));
        Assert.Equal(1, entries.Count(e => e.Kind == TargetKind.Ping));
    }

    // ── CheckResult ──

    [Fact]
    public void CheckResult_OkTrue_Constructed()
    {
        var r = new CheckResult
        {
            Key = "YouTube",
            Kind = TargetKind.Http,
            Value = "https://youtube.com",
            Ok = true,
            ElapsedMs = 120,
            StatusCode = 200
        };

        Assert.True(r.Ok);
        Assert.Equal(200, r.StatusCode);
        Assert.Equal(120, r.ElapsedMs);
    }

    // ── ProfileProbeResult ──

    [Fact]
    public void ProfileProbeResult_IsWorking_AboveThreshold()
    {
        var result = new ProfileProbeResult { Score = 80 };
        Assert.True(result.IsWorking(0.75));
        Assert.False(result.IsWorking(0.90));
    }

    [Fact]
    public void ProfileProbeResult_ShortProcessText_WhenNotStarted()
    {
        var result = new ProfileProbeResult { ProcessStarted = false };
        Assert.Contains("не найден", result.ShortProcessText);
    }

    [Fact]
    public void ProfileProbeResult_ShortProcessText_WhenStable()
    {
        var result = new ProfileProbeResult
        {
            ProcessStarted = true,
            ProcessStable = true,
            ProcessIds = new[] { 1234 }
        };
        Assert.Contains("OK", result.ShortProcessText);
        Assert.Contains("1234", result.ShortProcessText);
    }

    [Fact]
    public void ProfileProbeResult_FailedChecks_OnlyReturnsFailed()
    {
        var result = new ProfileProbeResult
        {
            Checks = new[]
            {
                new CheckResult { Key = "A", Ok = true },
                new CheckResult { Key = "B", Ok = false },
                new CheckResult { Key = "C", Ok = false }
            }
        };

        var failed = result.FailedChecks.ToList();
        Assert.Equal(2, failed.Count);
        Assert.All(failed, c => Assert.False(c.Ok));
    }

    // ── BAT-profile scanning from filesystem ──

    [Fact]
    public void ScanEngineDir_FindsBatFiles_ExcludesServiceBat()
    {
        File.WriteAllText(Path.Combine(_engineDir, "general.bat"), "@echo off");
        File.WriteAllText(Path.Combine(_engineDir, "discord.bat"), "@echo off");
        File.WriteAllText(Path.Combine(_engineDir, "service.bat"), "@echo off");

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat" };
        var profiles = Directory
            .EnumerateFiles(_engineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !excluded.Contains(Path.GetFileName(f)))
            .Select(f => new ProfileItem
            {
                FileName = Path.GetFileName(f),
                DisplayName = Path.GetFileNameWithoutExtension(f),
                FullPath = f
            })
            .OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(2, profiles.Count);
        Assert.DoesNotContain(profiles, p => p.FileName == "service.bat");
        Assert.Contains(profiles, p => p.FileName == "discord.bat");
        Assert.Contains(profiles, p => p.FileName == "general.bat");
    }

    [Fact]
    public void ScanEngineDir_EmptyDir_ReturnsEmpty()
    {
        var profiles = Directory
            .EnumerateFiles(_engineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .ToList();

        Assert.Empty(profiles);
    }

    // ── helpers ──

    private string WriteTargets(string content)
    {
        var path = Path.Combine(_engineDir, "targets.txt");
        File.WriteAllText(path, content);
        return path;
    }
}
