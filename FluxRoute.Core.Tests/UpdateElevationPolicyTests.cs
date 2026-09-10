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
    public void NeedsElevation_OnlyWhenProtectionSurvivedWithoutRights(
        bool alreadyElevated, bool protectionStillRunning, bool expected)
    {
        Assert.Equal(expected, UpdateElevationPolicy.NeedsElevation(alreadyElevated, protectionStillRunning));
    }

    [Theory]
    // Служба драйвера WinDivert запущена — снимать её придётся с правами.
    [InlineData("STATE : 4 RUNNING\r\n", 0, true)]
    // Драйвер выгружается: если не успеет дойти до STOPPED за время ожидания в StopEnginesBestEffort,
    // считаем его живым — батник ждёт освобождения драйвера и без прав не дождётся.
    [InlineData("STATE : 3 STOP_PENDING\r\n", 0, true)]
    [InlineData("STATE : 1 STOPPED\r\n", 0, false)]
    // Служба не установлена (sc query возвращает 1060) — права не нужны.
    [InlineData("", 1060, false)]
    // Состояние не получили (доступ, таймаут, пустой вывод) — считаем, что снимать придётся с правами:
    // тихий отказ BAT, который удаляет архив и запускает старую версию, хуже лишнего запроса UAC.
    [InlineData("", 5, true)]
    [InlineData(null, 0, true)]
    [InlineData("[SC] OpenService FAILED\r\n", 0, true)]
    public void IsServiceRunning_TreatsOnlyDefinitiveStopAsStopped(string? queryOutput, int exitCode, bool expected)
    {
        Assert.Equal(expected, UpdateElevationPolicy.IsServiceRunning(queryOutput, exitCode));
    }

    [Fact]
    public void PreparePortableLaunch_RequestsElevation_WhenEnginesSurvivedWithoutRights()
    {
        var psi = UpdateElevationPolicy.PreparePortableLaunch(
            @"C:\Temp\fluxroute-update.bat",
            isElevatedProbe: () => false,
            protectionRunningProbe: () => true);

        Assert.Equal("runas", psi.Verb);
    }

    [Fact]
    public void PreparePortableLaunch_KeepsUacOut_WhenEnginesAreAlreadyStopped()
    {
        var psi = UpdateElevationPolicy.PreparePortableLaunch(
            @"C:\Temp\fluxroute-update.bat",
            isElevatedProbe: () => false,
            protectionRunningProbe: () => false);

        Assert.True(string.IsNullOrEmpty(psi.Verb));
    }

    /// <summary>
    /// Проверка «что именно считаем остаточной защитой»: живая kernel-служба WinDivert — тот самый
    /// случай, который пропускала проверка только по именам процессов (Codex P1, ревью #76).
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void IsProtectionRunning_CountsDriverServiceAsSurvivingProtection(
        bool engineProcessRunning, bool driverServiceRunning, bool expected)
    {
        Assert.Equal(
            expected,
            UpdateElevationPolicy.IsProtectionRunning(() => engineProcessRunning, () => driverServiceRunning));
    }

    [Fact]
    public void PreparePortableLaunch_KeepsUacOut_WhenProcessIsAlreadyElevated()
    {
        var psi = UpdateElevationPolicy.PreparePortableLaunch(
            @"C:\Temp\fluxroute-update.bat",
            isElevatedProbe: () => true,
            protectionRunningProbe: () => true);

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
