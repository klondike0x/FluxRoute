using System.Text.Json;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для ModManager: сканирование, активация, деактивация, зависимости.
/// </summary>
public sealed class ModManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogger<ModManager> _logger;

    public ModManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteMods_{Guid.NewGuid():N}");
        _logger = NullLogger<ModManager>.Instance;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private IModManager CreateManager()
    {
        Directory.CreateDirectory(_tempDir);
        return new ModManager(_tempDir, _logger);
    }

    private async Task CreateModAsync(string folderName, string? startScript = null, string? stopScript = null,
        List<string>? deps = null)
    {
        var modDir = Path.Combine(_tempDir, folderName);
        Directory.CreateDirectory(modDir);

        var manifest = new ModManifest
        {
            Name = folderName,
            Version = "1.0.0",
            Author = "Test",
            Description = $"Test mod: {folderName}",
            Dependencies = deps ?? new List<string>(),
            Scripts = new ModScripts
            {
                Start = startScript,
                Stop = stopScript
            }
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(modDir, "manifest.json"), json);

        if (startScript != null)
        {
            var scriptContent = "@echo off\r\necho Hello from " + folderName + "\r\nexit /b 0\r\n";
            var scriptPath = Path.Combine(modDir, startScript.Split(' ')[0]);
            await File.WriteAllTextAsync(scriptPath, scriptContent);
        }

        if (stopScript != null)
        {
            var stopContent = "@echo off\r\necho Stop " + folderName + "\r\nexit /b 0\r\n";
            var stopPath = Path.Combine(modDir, stopScript.Split(' ')[0]);
            await File.WriteAllTextAsync(stopPath, stopContent);
        }
    }

    // ── ScanMods ──

    [Fact]
    public async Task ScanMods_EmptyDirectory_ReturnsEmptyList()
    {
        var mgr = CreateManager();
        var mods = await mgr.ScanModsAsync();
        Assert.Empty(mods);
    }

    [Fact]
    public async Task ScanMods_WithValidMod_ReturnsModInfo()
    {
        var mgr = CreateManager();
        await CreateModAsync("TestMod");

        var mods = await mgr.ScanModsAsync();

        Assert.Single(mods);
        Assert.Equal("TestMod", mods[0].FolderName);
        Assert.Equal("TestMod", mods[0].Name);
        Assert.Equal(ModStatus.Inactive, mods[0].Status);
    }

    [Fact]
    public async Task ScanMods_SkipsFolderWithoutManifest()
    {
        var mgr = CreateManager();
        var noManifestDir = Path.Combine(_tempDir, "NoManifest");
        Directory.CreateDirectory(noManifestDir);
        await CreateModAsync("ValidMod");

        var mods = await mgr.ScanModsAsync();

        Assert.Single(mods);
        Assert.Equal("ValidMod", mods[0].FolderName);
    }

    [Fact]
    public async Task ScanMods_InvalidJson_ReturnsErrorStatus()
    {
        var mgr = CreateManager();
        var modDir = Path.Combine(_tempDir, "BadJson");
        Directory.CreateDirectory(modDir);
        await File.WriteAllTextAsync(Path.Combine(modDir, "manifest.json"), "{ invalid json");

        var mods = await mgr.ScanModsAsync();

        Assert.Single(mods);
        Assert.Equal(ModStatus.Error, mods[0].Status);
        Assert.NotNull(mods[0].ErrorMessage);
    }

    // ── ActivateMod ──

    [Fact]
    public async Task ActivateMod_WithStartScript_SetsActive()
    {
        var mgr = CreateManager();
        await CreateModAsync("ActiveMod", startScript: "start.bat");
        await mgr.ScanModsAsync();

        var result = await mgr.ActivateModAsync("ActiveMod");

        Assert.True(result);
        Assert.Equal(ModStatus.Active, mgr.GetModStatus("ActiveMod"));
    }

    [Fact]
    public async Task ActivateMod_NoStartScript_ActivatesAsFilesMod()
    {
        var mgr = CreateManager();
        await CreateModAsync("NoStartMod");
        await mgr.ScanModsAsync();

        // Мод без скрипта (как импортированные из Zapret-Hub) активируется простым переключением
        var result = await mgr.ActivateModAsync("NoStartMod");

        Assert.True(result);
        Assert.Equal(ModStatus.Active, mgr.GetModStatus("NoStartMod"));
    }

    [Fact]
    public async Task ActivateMod_NonExistent_ReturnsFalse()
    {
        var mgr = CreateManager();
        var result = await mgr.ActivateModAsync("NonExistent");
        Assert.False(result);
    }

    // ── DeactivateMod ──

    [Fact]
    public async Task DeactivateMod_WithStopScript_SetsInactive()
    {
        var mgr = CreateManager();
        await CreateModAsync("DeactMod", startScript: "start.bat", stopScript: "stop.bat");
        await mgr.ScanModsAsync();
        await mgr.ActivateModAsync("DeactMod");

        var result = await mgr.DeactivateModAsync("DeactMod");

        Assert.True(result);
        Assert.Equal(ModStatus.Inactive, mgr.GetModStatus("DeactMod"));
    }

    [Fact]
    public async Task DeactivateMod_NoStopScript_SetsInactive()
    {
        var mgr = CreateManager();
        await CreateModAsync("NoStopMod", startScript: "start.bat");
        await mgr.ScanModsAsync();
        await mgr.ActivateModAsync("NoStopMod");

        var result = await mgr.DeactivateModAsync("NoStopMod");

        Assert.True(result);
        Assert.Equal(ModStatus.Inactive, mgr.GetModStatus("NoStopMod"));
    }

    // ── GetModStatus ──

    [Fact]
    public async Task GetModStatus_UnknownMod_ReturnsNotLoaded()
    {
        var mgr = CreateManager();
        Assert.Equal(ModStatus.NotLoaded, mgr.GetModStatus("Unknown"));
    }

    [Fact]
    public async Task ScanMods_LoadsStatusFromFile()
    {
        var mgr = CreateManager();
        await CreateModAsync("PersistMod", startScript: "start.bat");
        await mgr.ScanModsAsync();
        await mgr.ActivateModAsync("PersistMod");

        // Новый менеджер в той же папке должен прочитать статус из status.json
        var mgr2 = CreateManager();
        await mgr2.ScanModsAsync();

        Assert.Equal(ModStatus.Active, mgr2.GetModStatus("PersistMod"));
    }

    // ── CheckDependencies ──

    [Fact]
    public async Task CheckDependencies_AllActive_ReturnsTrue()
    {
        var mgr = CreateManager();
        await CreateModAsync("DepA", startScript: "start.bat");
        await CreateModAsync("DepB", startScript: "start.bat", deps: new List<string> { "DepA" });
        await mgr.ScanModsAsync();

        await mgr.ActivateModAsync("DepA");

        var depsOk = await mgr.CheckDependenciesAsync("DepB");
        Assert.True(depsOk);
    }

    [Fact]
    public async Task CheckDependencies_NotAllActive_ReturnsFalse()
    {
        var mgr = CreateManager();
        await CreateModAsync("DepA");
        await CreateModAsync("DepB", deps: new List<string> { "DepA" });
        await mgr.ScanModsAsync();

        var depsOk = await mgr.CheckDependenciesAsync("DepB");
        Assert.False(depsOk);
    }

    [Fact]
    public async Task ActivateMod_UnsatisfiedDependencies_Throws()
    {
        var mgr = CreateManager();
        await CreateModAsync("ModWithDep", startScript: "start.bat", deps: new List<string> { "MissingDep" });
        await mgr.ScanModsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.ActivateModAsync("ModWithDep"));
    }

    // ── CreateMod ──

    [Fact]
    public async Task CreateMod_CreatesFolderManifestAndScripts()
    {
        var mgr = CreateManager();

        var mod = await mgr.CreateModAsync("My Test Mod");

        Assert.Equal("my-test-mod", mod.FolderName);
        Assert.Equal("My Test Mod", mod.Name);

        var modDir = Path.Combine(_tempDir, "my-test-mod");
        Assert.True(Directory.Exists(modDir));
        Assert.True(File.Exists(Path.Combine(modDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(modDir, "start.bat")));
        Assert.True(File.Exists(Path.Combine(modDir, "stop.bat")));

        // Повторный скан должен найти новый мод
        var mods = await mgr.ScanModsAsync();
        Assert.Contains(mods, m => m.FolderName == "my-test-mod");
    }

    [Fact]
    public async Task CreateMod_DuplicateName_Throws()
    {
        var mgr = CreateManager();
        await mgr.CreateModAsync("TestMod");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.CreateModAsync("TestMod"));
    }

    [Fact]
    public async Task CreateMod_ThenActivate_Works()
    {
        var mgr = CreateManager();
        var mod = await mgr.CreateModAsync("NewMod");

        var ok = await mgr.ActivateModAsync(mod.FolderName);

        Assert.True(ok);
        Assert.Equal(ModStatus.Active, mgr.GetModStatus(mod.FolderName));
    }

    // ── Import ──

    [Fact]
    public async Task ImportFromFolder_CopiesContents()
    {
        var mgr = CreateManager();
        // Источник вне папки модов, чтобы не конфликтовали пути
        var srcRoot = Path.Combine(Path.GetTempPath(), $"FluxRouteSrc_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(srcRoot, "source-mod");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "manifest.json"),
            """{"name": "Source Mod", "version": "1.0.0", "scripts": {"start": "start.bat"}}""");
        try
        {
            var mod = await mgr.ImportFromFolderAsync(sourceDir);

            Assert.Equal("source-mod", mod.FolderName);
            Assert.True(File.Exists(Path.Combine(_tempDir, "source-mod", "data.txt")));
            Assert.True(File.Exists(Path.Combine(_tempDir, "source-mod", "manifest.json")));
        }
        finally
        {
            try { Directory.Delete(srcRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ImportFromFolder_NoManifest_CreatesOne()
    {
        var mgr = CreateManager();
        var srcRoot = Path.Combine(Path.GetTempPath(), $"FluxRouteSrc_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(srcRoot, "plain-folder");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "config.txt"), "x");
        try
        {
            var mod = await mgr.ImportFromFolderAsync(sourceDir);

            Assert.True(File.Exists(Path.Combine(_tempDir, "plain-folder", "manifest.json")));
            Assert.Equal("plain-folder", mod.FolderName);
        }
        finally
        {
            try { Directory.Delete(srcRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ImportFromFolder_NotExists_Throws()
    {
        var mgr = CreateManager();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => mgr.ImportFromFolderAsync(Path.Combine(_tempDir, "missing")));
    }

    [Fact]
    public async Task ImportFromZip_ExtractsAndCreatesManifest()
    {
        var mgr = CreateManager();
        var zipPath = Path.Combine(_tempDir, "archive-mod.zip");

        // Создаём zip с одним файлом
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("file.txt");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("content");
        }

        var mods = await mgr.ImportFromZipAsync(zipPath);

        Assert.Single(mods);
        var mod = mods[0];
        Assert.True(File.Exists(Path.Combine(_tempDir, mod.FolderName, "file.txt")));
        Assert.True(File.Exists(Path.Combine(_tempDir, mod.FolderName, "manifest.json")));
    }

    [Fact]
    public async Task ImportFromZip_Collection_ImportsEachMod()
    {
        var mgr = CreateManager();
        var zipPath = Path.Combine(_tempDir, "collection.zip");

        // Архив с двумя модами (коллекция как в peachoff/Zapret-Mods)
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var (dir, manifestName) in new[] { ("ModA", "manifest.json"), ("ModB", "zapret-hub-mod.json") })
            {
                var entry = zip.CreateEntry($"{dir}/{manifestName}");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    await writer.WriteAsync($"{{\"name\": \"{dir}\", \"version\": \"1.0.0\", \"slug\": \"{dir.ToLower()}\"}}");
                }
                var data = zip.CreateEntry($"{dir}/data.txt");
                using (var dataWriter = new StreamWriter(data.Open()))
                {
                    await dataWriter.WriteAsync("content");
                }
            }
        }

        var mods = await mgr.ImportFromZipAsync(zipPath);

        Assert.Equal(2, mods.Count);
        Assert.Contains(mods, m => m.FolderName == "moda");
        Assert.Contains(mods, m => m.FolderName == "modb");
        Assert.True(File.Exists(Path.Combine(_tempDir, "moda", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "modb", "manifest.json")));
    }

    [Fact]
    public async Task ImportFromFiles_CopiesFiles()
    {
        var mgr = CreateManager();
        var f1 = Path.Combine(_tempDir, "one.txt");
        var f2 = Path.Combine(_tempDir, "two.txt");
        await File.WriteAllTextAsync(f1, "1");
        await File.WriteAllTextAsync(f2, "2");

        var mod = await mgr.ImportFromFilesAsync(new[] { f1, f2 });

        var modDir = Path.Combine(_tempDir, mod.FolderName);
        Assert.True(File.Exists(Path.Combine(modDir, "one.txt")));
        Assert.True(File.Exists(Path.Combine(modDir, "two.txt")));
        Assert.True(File.Exists(Path.Combine(modDir, "manifest.json")));
    }

    [Fact]
    public async Task ImportFromFiles_Empty_Throws()
    {
        var mgr = CreateManager();
        await Assert.ThrowsAsync<ArgumentException>(
            () => mgr.ImportFromFilesAsync(Array.Empty<string>()));
    }
}
