namespace FluxRoute.Updater.Services;

/// <summary>Именованные HTTP-клиенты, зарегистрированные через IHttpClientFactory.</summary>
public static class HttpClientNames
{
    /// <summary>Клиент для загрузки обновлений движка Flowseal с GitHub.</summary>
    public const string Updater = "FluxRoute.Updater";

    /// <summary>Клиент для проверки и скачивания обновлений самого приложения FluxRoute.
    /// Настроен с авто-редиректами и увеличенным таймаутом для скачивания больших файлов.</summary>
    public const string AppUpdater = "FluxRoute.AppUpdater";
}
