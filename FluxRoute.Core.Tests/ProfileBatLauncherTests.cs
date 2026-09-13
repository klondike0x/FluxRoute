using System.IO;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Аргументы запуска winws.exe: пользовательские hostlist-файлы подмешиваются в план запуска.
/// Файл исключений может состоять из одной помеченной строки «!domain» — её сохраняют редактор и
/// синхронизация, — и такой файл обязан попасть в <c>--hostlist-exclude</c>: иначе единственное
/// исключение пользователя молча игнорируется (Codex P2, ревью pullrequestreview-5191690237).
/// В файле доменов пометки — вклад другого набора, поэтому файл без целевых доменов в
/// <c>--hostlist</c> не попадает.
/// </summary>
public sealed class ProfileBatLauncherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _engineDir;
    private readonly string _listsDir;
    private readonly string _batPath;
    private readonly string _excludePath;

    public ProfileBatLauncherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fluxroute-launcher-" + Guid.NewGuid().ToString("N"));
        _engineDir = Path.Combine(_tempDir, "engine");
        _listsDir = Path.Combine(_engineDir, "lists");
        Directory.CreateDirectory(_listsDir);

        // Запускаемый файл нужен, потому что план строится только для существующего winws.exe.
        File.WriteAllText(Path.Combine(_engineDir, "winws.exe"), string.Empty);
        File.WriteAllText(Path.Combine(_engineDir, "list.txt"), string.Empty);

        _batPath = Path.Combine(_engineDir, "general.bat");
        File.WriteAllText(
            _batPath,
            "start \"\" /min \"%~dp0winws.exe\" --wf-tcp=443 --filter-tcp=443"
            + Environment.NewLine
            + "exit /b" + Environment.NewLine);

        _excludePath = Path.Combine(_listsDir, "list-exclude-user.txt");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временная папка не критична для результата теста.
        }
    }

    private WinwsLaunchPlan CreatePlan()
    {
        var created = ProfileBatLauncher.TryCreateLaunchPlan(_batPath, _engineDir, out var plan, out var error);

        Assert.True(created, error);
        return plan!;
    }

    /// <summary>
    /// Сценарий находки: в файле исключений только помеченная строка. Такой файл обязан попасть
    /// в аргументы, иначе исключение не действует.
    /// </summary>
    [Fact]
    public void LaunchPlan_ExclusionFileWithOnlyMarkedLine_AddsHostlistExclude()
    {
        File.WriteAllText(_excludePath, "!tracker.example.com" + Environment.NewLine);

        var plan = CreatePlan();

        Assert.Contains("--hostlist-exclude", plan.Arguments);
        Assert.Contains(_excludePath, plan.Arguments);
    }

    [Fact]
    public void LaunchPlan_ExclusionFileWithPlainDomain_AddsHostlistExclude()
    {
        File.WriteAllText(_excludePath, "tracker.example.com" + Environment.NewLine);

        var plan = CreatePlan();

        Assert.Contains("--hostlist-exclude", plan.Arguments);
        Assert.Contains(_excludePath, plan.Arguments);
    }

    [Fact]
    public void LaunchPlan_ExclusionFileWithOnlyComments_DoesNotAddHostlistExclude()
    {
        File.WriteAllText(
            _excludePath,
            "# пока пусто" + Environment.NewLine + "; заметка" + Environment.NewLine);

        var plan = CreatePlan();

        Assert.DoesNotContain("--hostlist-exclude", plan.Arguments);
    }

    [Fact]
    public void LaunchPlan_GeneralFileWithOnlyMarkedLine_DoesNotAddHostlist()
    {
        File.WriteAllText(
            Path.Combine(_listsDir, "list-general-user.txt"),
            "!ads.example.com" + Environment.NewLine);

        var plan = CreatePlan();

        Assert.DoesNotContain("--hostlist", plan.Arguments);
    }

    [Fact]
    public void LaunchPlan_GeneralFileWithDomain_AddsHostlist()
    {
        File.WriteAllText(
            Path.Combine(_listsDir, "list-general-user.txt"),
            "youtube.com" + Environment.NewLine);

        var plan = CreatePlan();

        Assert.Contains("--hostlist", plan.Arguments);
    }
}
