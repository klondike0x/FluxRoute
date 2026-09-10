using System.Diagnostics;
using System.Security.Principal;

namespace FluxRoute.Updater.Services;

/// <summary>
/// Решает, нужно ли портативному bat-заменщику повышение прав.
/// Сам BAT гасит движки (`taskkill /IM winws.exe`, `taskkill /IM winws2.exe`,
/// `net stop WinDivert`) и ждёт освобождения драйвера, но делает это с правами того, кто его
/// запустил. Из приложения, работающего без повышения, `taskkill` не убьёт winws, запущенный
/// с правами администратора (частный случай: прошлый запуск с правами или неполное завершение),
/// а `net stop WinDivert` не остановит драйвер. BAT доходит до ветки отмены: скачанный архив и
/// распакованная папка удаляются, запускается СТАРАЯ версия, хотя DownloadAndApplyAsync уже
/// вернул успех и UI закрылся — пользователь видит прежнюю версию без объяснений
/// (Codex P1, ревью #76). Поэтому запрашиваем UAC ровно тогда, когда защита действительно
/// осталась запущена после попытки снять её без прав.
/// </summary>
public static class UpdateElevationPolicy
{
    /// <summary>Процессы, которые BAT обязан погасить перед заменой файлов (совпадает с taskkill в BAT).</summary>
    public static readonly string[] EngineProcessNames = ["winws", "winws2", "WinDivert"];

    /// <summary>
    /// true — запускать BAT с <c>Verb = "runas"</c>. Если процесс уже повышен, повторный запрос
    /// не нужен; если защита не запущена, обычная замена проходит без UAC.
    /// </summary>
    public static bool NeedsElevation(bool alreadyElevated, bool engineProcessStillRunning)
        => !alreadyElevated && engineProcessStillRunning;

    /// <summary>
    /// Готовит запуск портативного BAT: решение о правах → параметры запуска. Зонды передаются
    /// снаружи, чтобы тест проверял именно связку «защита осталась → Verb = runas»
    /// (<see cref="StopEnginesBestEffort"/> вызывается отдельно и в тестах не нужен).
    /// </summary>
    public static ProcessStartInfo PreparePortableLaunch(
        string batPath,
        Func<bool> isElevatedProbe,
        Func<bool> engineRunningProbe)
    {
        ArgumentNullException.ThrowIfNull(isElevatedProbe);
        ArgumentNullException.ThrowIfNull(engineRunningProbe);

        return CreateBatLaunch(batPath, NeedsElevation(isElevatedProbe(), engineRunningProbe()));
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
    /// Best-effort остановка движков без повышения прав: проходит для процессов того же
    /// пользователя и уровня целостности (обычный случай — защиту поднимало само приложение).
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
    }
}
