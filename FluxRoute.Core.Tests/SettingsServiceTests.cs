using System.IO;
using System.Text;
using System.Text.Json;
using FluxRoute.Core.Services;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для SettingsService: сохранение, загрузка, бэкап и обработка повреждённых файлов.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SettingsService CreateService() => new(_tempDir);

    // ── Load ──

    [Fact]
    public void Load_WhenNoFile_ReturnsDefaults()
    {
        var svc = CreateService();
        var settings = svc.Load();

        Assert.NotNull(settings);
        Assert.True(settings.SiteYouTube);
        Assert.True(settings.SiteDiscord);
        Assert.Equal("1", settings.OrchestratorInterval);
    }

    [Fact]
    public void Load_AfterSave_ReturnsSameValues()
    {
        var svc = CreateService();
        var original = new AppSettings
        {
            LastProfileFileName = "test.bat",
            OrchestratorInterval = "5",
            SiteYouTube = false,
            AutoUpdateEnabled = true
        };

        svc.Save(original);
        var loaded = svc.Load();

        Assert.Equal("test.bat", loaded.LastProfileFileName);
        Assert.Equal("5", loaded.OrchestratorInterval);
        Assert.False(loaded.SiteYouTube);
        Assert.True(loaded.AutoUpdateEnabled);
    }

    [Fact]
    public void Load_WhenPrimaryCorrupt_FallsBackToBackup()
    {
        var svc = CreateService();
        var good = new AppSettings { LastProfileFileName = "good.bat" };
        svc.Save(good); // создаёт primary
        svc.Save(good); // второй save создаёт backup через File.Replace

        // Портим primary
        File.WriteAllText(svc.SettingsPath, "{ NOT VALID JSON {{{{", Encoding.UTF8);

        var loaded = svc.Load();

        Assert.Equal("good.bat", loaded.LastProfileFileName);
    }

    [Fact]
    public void Load_WhenBothCorrupt_ReturnsDefaults()
    {
        var svc = CreateService();
        File.WriteAllText(svc.SettingsPath, "CORRUPT", Encoding.UTF8);
        File.WriteAllText(svc.BackupPath, "CORRUPT", Encoding.UTF8);

        var loaded = svc.Load();

        Assert.NotNull(loaded);
        Assert.True(loaded.SiteYouTube); // значение по умолчанию
    }

    // ── Save ──

    [Fact]
    public void Save_CreatesSettingsFile()
    {
        var svc = CreateService();
        svc.Save(new AppSettings());

        Assert.True(File.Exists(svc.SettingsPath));
    }

    [Fact]
    public void Save_CreatesBackupOnSecondSave()
    {
        var svc = CreateService();
        svc.Save(new AppSettings { OrchestratorInterval = "1" });
        svc.Save(new AppSettings { OrchestratorInterval = "2" });

        Assert.True(File.Exists(svc.BackupPath));
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        var svc = CreateService();
        svc.Save(new AppSettings { LastProfileFileName = "myprofile.bat" });

        var json = File.ReadAllText(svc.SettingsPath, Encoding.UTF8);
        var doc = JsonDocument.Parse(json);
        Assert.Equal("myprofile.bat",
            doc.RootElement.GetProperty("LastProfileFileName").GetString());
    }

    [Fact]
    public void Save_NullThrows()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentNullException>(() => svc.Save(null!));
    }

    [Fact]
    public void Save_WhenTargetDirectoryIsAFile_ThrowsIOException()
    {
        var blockedPath = Path.Combine(_tempDir, "blocked");
        File.WriteAllText(blockedPath, "not a directory");
        var svc = new SettingsService(blockedPath);

        Assert.Throws<IOException>(() => svc.Save(new AppSettings()));
    }

    // ── TgProxy round-trip ──

    [Fact]
    public void Save_TgProxySettings_RoundTrips()
    {
        var svc = CreateService();
        var settings = new AppSettings
        {
            TgProxy = new TgProxySettings
            {
                Host = "192.168.1.1",
                Port = 9090,
                Secret = "ddeadbeef00112233445566778899aabb",
                Verbose = true,
                CfProxyEnabled = false,
                AutoStartOnAppLaunch = false
            }
        };

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.Equal("192.168.1.1", loaded.TgProxy.Host);
        Assert.Equal(9090, loaded.TgProxy.Port);
        Assert.Equal("ddeadbeef00112233445566778899aabb", loaded.TgProxy.Secret);
        Assert.True(loaded.TgProxy.Verbose);
        Assert.False(loaded.TgProxy.CfProxyEnabled);
        Assert.False(loaded.TgProxy.AutoStartOnAppLaunch);
    }

    // ── DoH round-trip ──

    [Fact]
    public void Save_DohSettings_RoundTrips()
    {
        var svc = CreateService();
        var settings = new AppSettings
        {
            Doh = new DohSettings
            {
                Enabled = true,
                AutomaticSelection = false,
                EncryptionMode = DohEncryptionMode.EncryptedWithFallback,
                SelectedProviderId = "quad9",
                InterfaceName = "Ethernet"
            }
        };

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.True(loaded.Doh.Enabled);
        Assert.False(loaded.Doh.AutomaticSelection);
        Assert.Equal(DohEncryptionMode.EncryptedWithFallback, loaded.Doh.EncryptionMode);
        Assert.Equal("quad9", loaded.Doh.SelectedProviderId);
        Assert.Equal("Ethernet", loaded.Doh.InterfaceName);
    }

    [Fact]
    public void Save_DohOperationJournal_RoundTripsFamilyAndSystemState()
    {
        var svc = CreateService();
        var settings = new AppSettings();
        settings.Doh.OperationJournal = new DohOperationJournal
        {
            OperationId = Guid.NewGuid(),
            InterfaceName = "Ethernet",
            InterfaceId = "2E61C098-C635-4D56-AB43-E41ED7033B99",
            Ipv4 = new DnsFamilySnapshot(true, ["192.0.2.53"]),
            Ipv6 = new DnsFamilySnapshot(false, ["2001:db8::53"]),
            GlobalDohMode = DohGlobalMode.Disabled,
            ResolverMappings = [new DohResolverMappingSnapshot("1.1.1.1", true, "https://old.example/dns-query", false, true)],
            InterfaceMappings = [new DohInterfaceMappingSnapshot("1.1.1.1", false, true, "https://old.example/dns-query", 21)]
        };

        svc.Save(settings);
        var loaded = svc.Load().Doh.OperationJournal;

        Assert.NotNull(loaded);
        Assert.True(loaded.Ipv4.IsDhcp);
        Assert.False(loaded.Ipv6.IsDhcp);
        Assert.Equal(DohGlobalMode.Disabled, loaded.GlobalDohMode);
        Assert.Equal(21, loaded.InterfaceMappings.Single().Flags);
    }

    // ── ProfileRatings round-trip ──

    [Fact]
    public void Save_ProfileRatings_RoundTrips()
    {
        var svc = CreateService();
        var settings = new AppSettings
        {
            ProfileRatings = new List<ProfileRatingEntry>
            {
                new() { FileName = "a.bat", DisplayName = "A", Score = 85 },
                new() { FileName = "b.bat", DisplayName = "B", Score = 42 }
            }
        };

        svc.Save(settings);
        var loaded = svc.Load();

        Assert.Equal(2, loaded.ProfileRatings.Count);
        Assert.Equal(85, loaded.ProfileRatings[0].Score);
        Assert.Equal("b.bat", loaded.ProfileRatings[1].FileName);
    }

    // ── IsPortable ──

    [Fact]
    public void IsPortable_IsTrue()
    {
        var svc = CreateService();
        Assert.True(svc.IsPortable);
    }
}
