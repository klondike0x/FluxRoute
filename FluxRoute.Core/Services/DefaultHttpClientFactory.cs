using System.Net.Http;

namespace FluxRoute.Core.Services;

/// <summary>
/// Минимальная реализация IHttpClientFactory для использования вне DI-контейнера
/// (WPF designer, юнит-тесты).
/// </summary>
public sealed class DefaultHttpClientFactory : IHttpClientFactory
{
    private static readonly DefaultHttpClientFactory _instance = new();
    public static DefaultHttpClientFactory Instance => _instance;

    public HttpClient CreateClient(string name)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }
}
