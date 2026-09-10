using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace FluxRoute.Updater.Services;

/// <summary>
/// Решает, нужно ли портативному bat-заменщику повышение прав.
/// Сам BAT гасит движки (`taskkill /IM winws.exe`, `taskkill /IM winws2.exe`,
/// `net stop WinDivert`) и ждёт освобождения драйвера, но делает это с правами того, кто его
/// запустил. Из приложения, работающего без повышения, `taskkill` не убьёт winws, запущенный
/// с правами администратора (частный случай: прошлый запуск с правами или неполное завершение),
/// а `net stop WinDivert` не остановит драйвер.
///
/// Проверять нужно оба вида защиты: процессы движка и kernel-службу драйвера. У службы WinDivert
/// нет собственного процесса (файла `WinDivert.exe` в системе обычно нет), поэтому проверка только
/// по именам процессов считала её снятой, BAT запускался без `runas`, не мог выполнить
/// `net stop WinDivert` и доходил до ветки отмены: скачанный архив и распакованная папка удалялись,
/// запускалась СТАРАЯ версия, хотя DownloadAndApplyAsync уже вернул успех и UI закрылся
/// (Codex P1, ревью #76). Поэтому запрашиваем UAC ровно тогда, когда защита действительно
/// осталась запущена после попытки снять её без прав.
/// </summary>
public static class UpdateElevationPolicy
{
    /// <summary>Процессы, которые BAT обязан погасить перед заменой файлов (совпадает с taskkill в BAT).</summary>
    public static readonly string[] EngineProcessNames = ["winws", "winws2", "WinDivert"];

    /// <summary>
    /// Kernel-служба драйвера WinDivert: отдельная сущность SCM. `taskkill` её не снимает,
    /// без прав не проходит `net stop WinDivert`, а `sc query` — единственный способ увидеть,
    /// что драйвер ещё загружен (<see cref="UpdaterService.StopZapretService"/> останавливает её
    /// независимо от winws).
    /// </summary>
    public const string DriverServiceName = "WinDivert";

    /// <summary>Код `sc query` «служба не установлена»: драйвера нет, права не нужны.</summary>
    private const int ServiceNotInstalledExitCode = 1060;

    /// <summary>
    /// true — запускать BAT с <c>Verb = "runas"</c>. Если процесс уже повышен, повторный запрос
    /// не нужен; если защита не запущена, обычная замена проходит без UAC.
    /// </summary>
    public static bool NeedsElevation(bool alreadyElevated, bool protectionStillRunning)
        => !alreadyElevated && protectionStillRunning;

    /// <summary>
    /// Готовит запуск портативного BAT: решение о правах → параметры запуска. Зонды передаются
    /// снаружи, чтобы тест проверял именно связку «защита осталась → Verb = runas»
    /// (<see cref="StopEnginesBestEffort"/> вызывается отдельно и в тестах не нужен).
    /// </summary>
    public static ProcessStartInfo PreparePortableLaunch(
        string batPath,
        Func<bool> isElevatedProbe,
        Func<bool> protectionRunningProbe)
    {
        ArgumentNullException.ThrowIfNull(isElevatedProbe);
        ArgumentNullException.ThrowIfNull(protectionRunningProbe);

        return CreateBatLaunch(batPath, NeedsElevation(isElevatedProbe(), protectionRunningProbe()));
    }

    /// <summary>Запущен ли текущий процесс с правами администратора.</summary>
    public static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // Не смогли определить — считаем, что прав нет: лишний UAC безопаснее тихого отказа.
            return false;
        }
    }

    /// <summary>
    /// Собирает запуск BAT-заменщика: с запросом прав, когда защиту не удалось снять без них.
    /// Отдельный метод — чтобы тесты проверяли именно проброс <c>Verb</c>, а не только решение.
    /// </summary>
    public static ProcessStartInfo CreateBatLaunch(string batPath, bool needsElevation)
        => new()
        {
            FileName        = batPath,
            WindowStyle     = ProcessWindowStyle.Hidden,
            UseShellExecute = true,
            Verb            = needsElevation ? "runas" : string.Empty
        };

    /// <summary>
    /// Осталась ли защита, которую BAT не снимёт без прав: процесс движка ИЛИ служба драйвера
    /// WinDivert. Именно эта связка решает, нужен ли `runas` (Codex P1, ревью #76).
    /// </summary>
    public static bool IsProtectionRunning() => IsProtectionRunning(IsEngineProcessRunning, IsDriverServiceRunning);

    /// <summary>
    /// Та же проверка с инжектируемыми зондами — чтобы тест фиксировал именно состав защиты:
    /// проверки только по именам процессов недостаточно, у kernel-службы WinDivert нет процесса.
    /// </summary>
    public static bool IsProtectionRunning(Func<bool> engineProcessProbe, Func<bool> driverServiceProbe)
    {
        ArgumentNullException.ThrowIfNull(engineProcessProbe);
        ArgumentNullException.ThrowIfNull(driverServiceProbe);

        return engineProcessProbe() || driverServiceProbe();
    }

    /// <summary>Остались ли запущенные процессы движка или драйвера WinDivert.</summary>
    public static bool IsEngineProcessRunning()
    {
        foreach (var name in EngineProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 0)
                        return true;
                }
                finally
                {
                    foreach (var process in processes)
                        process.Dispose();
                }
            }
            catch
            {
                // Нет доступа к списку процессов — не повод срывать обновление.
            }
        }

        return false;
    }

    /// <summary>
    /// Загружена ли служба драйвера WinDivert. Состояние спрашиваем у SCM: процесса у kernel-драйвера
    /// нет, поэтому <see cref="IsEngineProcessRunning"/> его не видит.
    /// </summary>
    public static bool IsDriverServiceRunning()
    {
        try
        {
            using var query = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", $"query \"{DriverServiceName}\"")
                {
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            query.Start();
            var output = query.StandardOutput.ReadToEnd();
            if (!query.WaitForExit(5000))
            {
                try { query.Kill(entireProcessTree: true); } catch { }
                return true;
            }

            return IsServiceRunning(output, query.ExitCode);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Классификация вывода `sc query`: true — службу придётся останавливать с правами.
    /// Определённый ответ даёт только `STOPPED` (или код 1060 — служба не установлена). `RUNNING`,
    /// `STOP_PENDING`, пустой и непонятный вывод считаем «осталась запущенной»: тихий отказ BAT,
    /// который удаляет скачанный архив и подсовывает старую версию, хуже лишнего запроса UAC.
    /// </summary>
    public static bool IsServiceRunning(string? queryOutput, int exitCode)
    {
        if (exitCode == ServiceNotInstalledExitCode)
            return false;

        if (string.IsNullOrWhiteSpace(queryOutput))
            return true;

        return !queryOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Best-effort остановка движков без повышения прав: проходит для процессов того же
    /// пользователя и уровня целостности (обычный случай — защиту поднимало само приложение).
    /// Службу драйвера пробуем остановить там же, чтобы в непривилегированном сценарии не
    /// запрашивать UAC там, где защита действительно снимается.
    /// Если защиту поднимали с правами администратора, вызовы молча не сработают, и тогда
    /// <see cref="NeedsElevation"/> отправит BAT на путь с UAC.
    /// </summary>
    public static void StopEnginesBestEffort()
    {
        foreach (var name in EngineProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Процесс с повышением прав — его снимет BAT с Verb = "runas".
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Список процессов недоступен — делегируем очистку BAT.
            }
        }

        StopDriverServiceBestEffort();
    }

    /// <summary>
    /// Пробует остановить службу WinDivert без повышения и ждёт перехода в STOPPED до трёх секунд,
    /// чтобы <see cref="IsDriverServiceRunning"/> не считал выгружающийся драйвер живым
    /// (у WinDivert выгрузка занимает несколько секунд).
    /// </summary>
    private static void StopDriverServiceBestEffort()
    {
        try
        {
            using var stop = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", $"stop \"{DriverServiceName}\"")
                {
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            stop.Start();
            stop.WaitForExit(10000);
        }
        catch
        {
            // Прав нет — службу снимет BAT с Verb = "runas".
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && IsDriverServiceRunning())
            Thread.Sleep(250);
    }
}
