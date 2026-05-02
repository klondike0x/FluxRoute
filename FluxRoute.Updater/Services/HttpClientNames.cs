namespace FluxRoute.Updater.Services;

/// <summary>Именованные HTTP-клиенты, зарегистрированные через IHttpClientFactory.</summary>
public static class HttpClientNames
{
    /// <summary>Клиент для загрузки обновлений Flowseal с GitHub.</summary>
    public const string Updater = "FluxRoute.Updater";
}
