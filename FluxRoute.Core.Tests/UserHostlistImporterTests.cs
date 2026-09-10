using System.IO;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Разбор пользовательских hostlist-файлов (Codex P2, ревью #76). Ключевое требование: строка
/// «!domain» — исключение. Раньше префикс отбрасывался, домен попадал в целевые, синхронизация
/// переписывала его в файл без пометки, и явное исключение становилось включением.
/// </summary>
public sealed class UserHostlistImporterTests : IDisposable
{
    private readonly string _tempDir;

    public UserHostlistImporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteHostlistImport_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>Нормализация как в UI: без протокола, «www.» и пути.</summary>
    private static string Normalize(string input)
    {
        var value = (input ?? "").Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = value["https://".Length..];
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = value["http://".Length..];
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            value = value["www.".Length..];

        var cut = value.IndexOfAny(['/', '?', '#', ':']);
        return cut >= 0 ? value[..cut] : value;
    }

    [Fact]
    public void GeneralFile_MarkedDomain_GoesToExcludes_NotToTargets()
    {
        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            "youtube.com\n!ads.example.com\ndiscord.com\n",
            Normalize);

        Assert.Equal(["youtube.com", "discord.com"], result.Targets);
        Assert.Equal(["ads.example.com"], result.Excludes);
    }

    [Fact]
    public void MarkedDomain_IsNeverTarget_EvenWhenSameDomainIsUnmarkedToo()
    {
        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            "ads.example.com\n!ads.example.com\n",
            Normalize);

        Assert.Empty(result.Targets);
        Assert.Equal(["ads.example.com"], result.Excludes);
    }

    [Fact]
    public void ExclusionFile_AllLinesAreExclusions_EvenWithoutMarker()
    {
        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.ExclusionFileName,
            "ads.example.com\n!tracker.example.com\n",
            Normalize);

        Assert.Empty(result.Targets);
        Assert.Equal(["ads.example.com", "tracker.example.com"], result.Excludes);
    }

    [Fact]
    public void Comments_AndEmptyLines_AreSkipped()
    {
        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            "# комментарий\n; ещё\n\n   \nyoutube.com\n",
            Normalize);

        Assert.Equal(["youtube.com"], result.Targets);
        Assert.Empty(result.Excludes);
    }

    [Fact]
    public void Normalizer_ReceivesDomainWithoutMarker()
    {
        var seen = new List<string>();

        UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            "!https://www.Ads.Example.com/path?x=1\n",
            input => { seen.Add(input); return Normalize(input); });

        Assert.Equal(["https://www.Ads.Example.com/path?x=1"], seen);
    }

    [Fact]
    public void Duplicates_AreDeduplicated_CaseInsensitively()
    {
        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            "YouTube.com\nyoutube.com\n!Ads.example.com\n!ads.EXAMPLE.com\n",
            Normalize);

        Assert.Equal(["YouTube.com"], result.Targets);
        Assert.Equal(["Ads.example.com"], result.Excludes);
    }

    /// <summary>
    /// Файл, где есть только помеченная строка, не переписывается: целевой набор пуст, а метки
    /// синхронизация при сравнении игнорирует. Раньше домен попадал в целевые, сравнение видело
    /// расхождение и перезаписывало файл обычным доменом — исключение становилось включением.
    /// </summary>
    [Fact]
    public void MarkedLineOnly_KeepsFileUntouched_AndDomainStaysAnExclusion()
    {
        var path = Path.Combine(_tempDir, UserHostlistImporter.TargetFileName);
        File.WriteAllText(path, "!ads.example.com\n");

        var result = UserHostlistImporter.Classify(
            UserHostlistImporter.TargetFileName,
            File.ReadAllText(path),
            Normalize);

        Assert.Empty(result.Targets);
        Assert.Equal(["ads.example.com"], result.Excludes);
        Assert.False(HostlistSyncPolicy.NeedsWrite(path, result.Targets, result.Targets.Count == 0));
    }
}
