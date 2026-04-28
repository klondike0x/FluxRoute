using System;

namespace FluxRoute.Core.Models;

public enum AppLogCategory
{
    App,
    Orchestrator,
    ProfileScan,
    Process,
    TgProxy,
    Updater,
    Service,
    Error
}

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

public sealed class AppLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public AppLogCategory Category { get; init; } = AppLogCategory.App;
    public AppLogLevel Level { get; init; } = AppLogLevel.Info;
    public string Message { get; init; } = string.Empty;

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string CategoryText => Category switch
    {
        AppLogCategory.App => "Приложение",
        AppLogCategory.Orchestrator => "Оркестратор",
        AppLogCategory.ProfileScan => "Сканирование профилей",
        AppLogCategory.Process => "Запуск профиля / winws.exe",
        AppLogCategory.TgProxy => "TG WS Proxy",
        AppLogCategory.Updater => "Обновление engine",
        AppLogCategory.Service => "Сервис",
        AppLogCategory.Error => "Ошибки",
        _ => Category.ToString()
    };

    public string LevelText => Level switch
    {
        AppLogLevel.Warning => "WARN",
        AppLogLevel.Error => "ERROR",
        _ => "INFO"
    };

    public string DisplayText => $"[{TimeText}] [{CategoryText}] [{LevelText}] {Message}";

    public override string ToString() => DisplayText;
}
