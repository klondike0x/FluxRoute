using System.Diagnostics;
using FluxRoute.Updater.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Портативное обновление без прав администратора (Codex P1, ревью #76): BAT сам гасит winws/WinDivert,
/// но из непривилегированного процесса он их не остановит, дойдёт до ветки отмены и молча запустит
/// прежнюю версию. Здесь проверяется решение о повышении прав и то, что оно реально доезжает
/// до запуска BAT (<c>Verb = "runas"</c>).
/// </summary>
public sealed class UpdateElevationPolicyTests
{
    [Theory]
    // Защита осталась запущенной, прав нет — только повышение спасёт замену файлов.
    [InlineData(false, true, true)]
    // Обычный случай: приложение уже сняло защиту — обновление идёт без UAC.
    [InlineData(false, false, false)]
    // Процесс уже повышен — повторный запрос прав не нужен.
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void NeedsElevation_OnlyWhenEnginesSurvivedWithoutRights(
        bool alreadyElevated, bool engineProcessStillRunning, bool expected)
    {
        Assert.Equal(expected, UpdateElevationPolicy.NeedsElevation(alreadyElevated, engineProcessStillRunning));
    }

    [Fact]
    public void PreparePortableLaunch_RequestsElevation_WhenEnginesSurvivedWithoutRights()
    {
        var psi = UpdateElevationPolicy.PreparePortableLaunch(
            @"C:\Temp\fluxroute-update.bat",
            isElevatedProbe: () => false,
            engineRunningProbe: () => true);

        Assert.Equal("runas", psi.Verb);
    }

    [Fact]
    public void PreparePortableLaunch_KeepsUacOut_WhenEnginesAreAlreadyStopped()
    {
        var psi = UpdateElevationPolicy.PreparePortableLaunch(
            @"C:\Temp\fluxroute-update.bat",
            isElevatedProbe: () => false,
            engineRunningProbe: () => false);

        Assert.True(string.IsNullOrEmpty(psi.Verb));
    }

    [Fact]
    public void CreateBatLaunch_RequestsElevation_WhenCleanupNeedsIt()
    {
        var psi = UpdateElevationPolicy.CreateBatLaunch(@"C:\Temp\fluxroute-update.bat", needsElevation: true);

        Assert.Equal("runas", psi.Verb);
        Assert.True(psi.UseShellExecute);
        Assert.Equal(ProcessWindowStyle.Hidden, psi.WindowStyle);
    }

    [Fact]
    public void CreateBatLaunch_RunsWithoutUac_WhenEnginesAreAlreadyStopped()
    {
        var psi = UpdateElevationPolicy.CreateBatLaunch(@"C:\Temp\fluxroute-update.bat", needsElevation: false);

        Assert.True(string.IsNullOrEmpty(psi.Verb));
        Assert.True(psi.UseShellExecute);
    }
}
