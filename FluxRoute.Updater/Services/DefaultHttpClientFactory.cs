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
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
        return client;
    }
}
