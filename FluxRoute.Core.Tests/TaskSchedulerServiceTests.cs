using FluxRoute.Core.Services;
using FluxRoute.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// v1.6.0: Тесты для TaskSchedulerService, новых свойств AppSettings
/// и логики синхронизации доменов.
/// </summary>
public sealed class TaskSchedulerServiceTests
{
    // ═══ TaskSchedulerService ═══

    [Fact]
    public void CreateTask_DoesNotThrow()
    {
        var svc = new TaskSchedulerService();
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        // Создание задачи не должно падать (может требовать админ-права,
        // но вызов CreateTask должен обрабатываться без исключений)
        var ex = Record.Exception(() => svc.CreateTask(exePath));

        // Ожидаем успех или SecurityException (нет прав админа)
        if (ex is not null)
            Assert.IsType<System.UnauthorizedAccessException>(ex);
    }

    [Fact]
    public void IsTaskExists_ReturnsFalse_WhenNoTask()
    {
        var svc = new TaskSchedulerService();
        Assert.False(svc.IsTaskExists());
    }

    [Fact]
    public void RemoveTask_DoesNotThrow_WhenNoTask()
    {
        var svc = new TaskSchedulerService();
        var ex = Record.Exception(() => svc.RemoveTask());
        Assert.Null(ex);
    }

    [Fact]
    public void CreateAndRemoveTask()
    {
        var svc = new TaskSchedulerService();
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        try
        {
            svc.CreateTask(exePath);
            Assert.True(svc.IsTaskExists());
        }
        catch (System.UnauthorizedAccessException)
        {
            // Нет прав админа — пропускаем проверку создания
            return;
        }
        finally
        {
            svc.RemoveTask();
            Assert.False(svc.IsTaskExists());
        }
    }

    // ═══ AppSettings — новые свойства v1.6.0 ═══

    [Fact]
    public void AppSettings_NewProperties_HaveCorrectDefaults()
    {
        var settings = new AppSettings();

        Assert.False(settings.TaskSchedulerAutoStart);
        Assert.False(settings.AutoLaunchProfile);
        Assert.True(settings.SyncDomainsWithUI); // По умолчанию включено
    }

    [Fact]
    public void AppSettings_NewProperties_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SettingsService(tempDir);
            var original = new AppSettings
            {
                TaskSchedulerAutoStart = true,
                AutoLaunchProfile = true,
                SyncDomainsWithUI = false,
                LastProfileFileName = "test-profile.bat"
            };

            svc.Save(original);
            var loaded = svc.Load();

            Assert.True(loaded.TaskSchedulerAutoStart);
            Assert.True(loaded.AutoLaunchProfile);
            Assert.False(loaded.SyncDomainsWithUI);
            Assert.Equal("test-profile.bat", loaded.LastProfileFileName);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AppSettings_NewProperties_PreserveAfterMinimalSave()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var svc = new SettingsService(tempDir);
            // Сохраняем только базовые значения
            svc.Save(new AppSettings { AutoLaunchProfile = true });

            var loaded = svc.Load();
            Assert.True(loaded.AutoLaunchProfile);
            Assert.False(loaded.TaskSchedulerAutoStart); // дефолт
            Assert.True(loaded.SyncDomainsWithUI); // дефолт (true)
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
