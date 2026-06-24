using System.Net.Http;
using Microsoft.Extensions.Http;

namespace FluxRoute.Updater.Services;

/// <summary>
/// Минимальная реализация IHttpClientFactory для использования вне DI-контейнера
/// (WPF designer, юнит-тесты, консольные инструменты).
/// Создаёт клиент со стандартным SocketsHttpHandler и заголовком User-Agent.
/// </summary>
internal sealed class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        // AppUpdater требует авто-редиректы (GitHub → CDN) и большой таймаут для ZIP-файлов
        if (name == HttpClientNames.AppUpdater)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                AllowAutoRedirect        = true,
                MaxAutomaticRedirections = 10,
                EnableMultipleHttp2Connections = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-AppUpdater");
            return client;
        }

        // Клиент для скачивания ZIP-архивов движка (большой таймаут + явный SSL)
        if (name == HttpClientNames.UpdaterDownload)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                AllowAutoRedirect        = true,
                MaxAutomaticRedirections = 10,
                EnableMultipleHttp2Connections = true,
                // SocketsHttpHandler НЕ использует ServicePointManager — задаём явно
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols =
                        System.Security.Authentication.SslProtocols.Tls12 |
                        System.Security.Authentication.SslProtocols.Tls13
                }
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
            return client;
        }

        // Стандартный клиент для проверки версии движка Flowseal
        var defaultHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true
        };
        var defaultClient = new HttpClient(defaultHandler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        defaultClient.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
        return defaultClient;
    }
}
