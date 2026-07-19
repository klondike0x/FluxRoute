using Microsoft.Win32.TaskScheduler;

namespace FluxRoute.Services;

/// <summary>
/// Интерфейс для управления задачей автозапуска через Планировщик задач Windows.
/// Надёжнее реестра — не блокируется антивирусами.
/// </summary>
public interface ITaskSchedulerService
{
    /// <summary>Создаёт задачу автозапуска FluxRoute при входе пользователя.</summary>
    /// <param name="exePath">Путь к исполняемому файлу.</param>
    /// <param name="highPriority">Установить высокий приоритет задачи (AboveNormal).</param>
    /// <param name="delaySeconds">Задержка запуска в секундах после входа в систему.</param>
    void CreateTask(string exePath, bool highPriority = false, int delaySeconds = 30);

    /// <summary>Удаляет задачу автозапуска.</summary>
    void RemoveTask();

    /// <summary>Проверяет, существует ли задача автозапуска.</summary>
    bool IsTaskExists();
}

/// <summary>
/// Реализация <see cref="ITaskSchedulerService"/> через Microsoft.Win32.TaskScheduler.
/// Использует TaskService для локального подключения к планировщику.
/// </summary>
public sealed class TaskSchedulerService : ITaskSchedulerService
{
    private const string TaskName = "FluxRoute AutoStart";
    private const string TaskDescription = "Автозапуск FluxRoute при входе пользователя в систему.";

    /// <inheritdoc/>
    public void CreateTask(string exePath, bool highPriority = false, int delaySeconds = 30)
    {
        using var ts = new TaskService();

        // Удаляем старую задачу, если есть
        var existing = ts.FindTask(TaskName, false);
        if (existing is not null)
        {
            ts.RootFolder.DeleteTask(TaskName, false);
        }

        var td = ts.NewTask();
        td.RegistrationInfo.Description = TaskDescription;
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Principal.LogonType = TaskLogonType.InteractiveToken;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // без ограничения
        td.Settings.AllowHardTerminate = false;

        // ═══ v1.6.0: Приоритет задачи ═══
        td.Settings.Priority = highPriority
            ? System.Diagnostics.ProcessPriorityClass.AboveNormal
            : System.Diagnostics.ProcessPriorityClass.Normal;

        // Триггер: при входе любого пользователя
        var logonTrigger = new LogonTrigger();

        // ═══ v1.6.0: Отложенный запуск ═══
        if (delaySeconds > 0)
        {
            logonTrigger.Delay = TimeSpan.FromSeconds(delaySeconds);
        }

        td.Triggers.Add(logonTrigger);

        // Действие: запуск FluxRoute.exe --minimized
        td.Actions.Add(new ExecAction(exePath, "--minimized", null));

        ts.RootFolder.RegisterTaskDefinition(TaskName, td);
    }

    /// <inheritdoc/>
    public void RemoveTask()
    {
        try
        {
            using var ts = new TaskService();
            var task = ts.FindTask(TaskName, false);
            if (task is not null)
            {
                ts.RootFolder.DeleteTask(TaskName, false);
            }
        }
        catch
        {
            // Игнорируем ошибки удаления — задача может быть уже удалена или недоступна
        }
    }

    /// <inheritdoc/>
    public bool IsTaskExists()
    {
        try
        {
            using var ts = new TaskService();
            return ts.FindTask(TaskName, false) is not null;
        }
        catch
        {
            return false;
        }
    }
}
