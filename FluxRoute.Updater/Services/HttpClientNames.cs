namespace FluxRoute.Updater.Services;

/// <summary>Именованные HTTP-клиенты, зарегистрированные через IHttpClientFactory.</summary>
public static class HttpClientNames
{
    /// <summary>Клиент для проверки версии обновлений движка Flowseal с GitHub (короткие запросы).</summary>
    public const string Updater = "FluxRoute.Updater";

    /// <summary>Клиент для скачивания ZIP-архива обновлений движка (большие файлы, увеличенный таймаут).</summary>
    public const string UpdaterDownload = "FluxRoute.UpdaterDownload";

    /// <summary>Клиент для проверки и скачивания обновлений самого приложения FluxRoute.</summary>
    public const string AppUpdater = "FluxRoute.AppUpdater";
}
