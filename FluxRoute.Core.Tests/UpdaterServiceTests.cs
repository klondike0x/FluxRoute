using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using FluxRoute.Updater.Services;
using Moq;
using Moq.Protected;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для UpdaterService: GetLocalVersion, NormalizeVersion, GetLatestReleaseAsync (mock).
/// Сетевые тесты намеренно пропущены (не запускаются в CI без сети).
/// </summary>
public sealed class UpdaterServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UpdaterService _svc;

    public UpdaterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteUpdaterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _svc = new UpdaterService(); // uses DefaultHttpClientFactory
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── GetLocalVersion ──

    [Fact]
    public void GetLocalVersion_NoFiles_ReturnsUnknown()
    {
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetLocalVersion_FromVersionTxt_ReturnsNormalized()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "  v1.9.7b  ");
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.9.7b", result);
    }

    [Fact]
    public void GetLocalVersion_FromVersionTxt_StripsVPrefix()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "V2.0.0");
        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtEmpty_FallsBackToServiceBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "  ");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "echo hi\r\nset LOCAL_VERSION=1.8.0\r\necho done\r\n");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.8.0", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtUnknown_FallsBackToServiceBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "unknown");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "set \"LOCAL_VERSION=1.7.3\"");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.7.3", result);
    }

    [Fact]
    public void GetLocalVersion_ServiceBatQuotedVersion_Parsed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "set \"LOCAL_VERSION=1.9.5a\"");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("1.9.5a", result);
    }

    [Fact]
    public void GetLocalVersion_NoVersionInBat_ReturnsUnknown()
    {
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"),
            "@echo off\r\necho No version here\r\n");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetLocalVersion_VersionTxtTakesPriorityOverBat()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.txt"), "2.0.0");
        File.WriteAllText(Path.Combine(_tempDir, "service.bat"), "set LOCAL_VERSION=1.0.0");

        var result = _svc.GetLocalVersion(_tempDir);
        Assert.Equal("2.0.0", result); // version.txt всегда в приоритете
    }

    // ── GetLatestReleaseAsync: timeout handling ──

    [Fact]
    public async Task GetLatestReleaseAsync_TaskCanceled_ReturnsFriendlyError()
    {
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException()));

        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.Updater);

        var (update, error) = await svc.GetLatestReleaseAsync(default);

        Assert.Null(update);
        Assert.Contains("Сервер GitHub не отвечает", error);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_HttpRequestTimeout_ReturnsFriendlyError()
    {
        var timeoutEx = new HttpRequestException("timeout", new TimeoutException());
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(timeoutEx));

        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.Updater);

        var (update, error) = await svc.GetLatestReleaseAsync(default);

        Assert.Null(update);
        Assert.Contains("Сервер GitHub не отвечает", error);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Success_ReturnsUpdateInfo()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("1.9.7b")
        };
        var handlerMock = CreateHandlerMock((req, ct) => Task.FromResult(response));
        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.Updater);

        var (update, error) = await svc.GetLatestReleaseAsync(default);

        Assert.Null(error);
        Assert.NotNull(update);
        Assert.Equal("1.9.7b", update!.Version);
        Assert.Contains("1.9.7b", update.DownloadUrl);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_EmptyVersion_ReturnsError()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("  ")
        };
        var handlerMock = CreateHandlerMock((req, ct) => Task.FromResult(response));
        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.Updater);

        var (update, error) = await svc.GetLatestReleaseAsync(default);

        Assert.Null(update);
        Assert.Contains("Пустая версия", error);
    }

    // ── InstallUpdateAsync: timeout handling ──

    [Fact]
    public async Task InstallUpdateAsync_TaskCanceledDuringDownload_ReturnsFalseWithFriendlyMessage()
    {
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException()));

        var (svc, factoryMock) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.UpdaterDownload);

        var messages = new List<string>();
        var update = new UpdateInfo
        {
            Version = "1.9.7b",
            DownloadUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.9.7b/zapret-discord-youtube-1.9.7b.zip"
        };

        var result = await svc.InstallUpdateAsync(_tempDir, update, msg => messages.Add(msg), default);

        Assert.False(result);
        Assert.Contains(messages, m => m.Contains("Сервер GitHub не отвечает"));
    }

    [Fact]
    public async Task InstallUpdateAsync_HttpRequestTimeoutDuringDownload_ReturnsFalse()
    {
        var timeoutEx = new HttpRequestException("timeout", new TimeoutException());
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(timeoutEx));

        var (svc, factoryMock) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.UpdaterDownload);

        var messages = new List<string>();
        var update = new UpdateInfo
        {
            Version = "1.9.7b",
            DownloadUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.9.7b/zapret-discord-youtube-1.9.7b.zip"
        };

        var result = await svc.InstallUpdateAsync(_tempDir, update, msg => messages.Add(msg), default);

        Assert.False(result);
        Assert.Contains(messages, m => m.Contains("Сервер GitHub не отвечает"));
    }

    // ── InstallUpdateAsync: SSL error handling ──

    [Fact]
    public async Task InstallUpdateAsync_SslError_ReturnsFriendlyMessage()
    {
        var authEx = new HttpRequestException("SSL error", new AuthenticationException("Remote certificate is invalid"));
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(authEx));

        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.UpdaterDownload);

        var messages = new List<string>();
        var update = new UpdateInfo
        {
            Version = "1.9.7b",
            DownloadUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.9.7b/zapret-discord-youtube-1.9.7b.zip"
        };

        var result = await svc.InstallUpdateAsync(_tempDir, update, msg => messages.Add(msg), default);

        Assert.False(result);
        Assert.Contains(messages, m => m.Contains("защищённое соединение"));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_SslError_ReturnsFriendlyError()
    {
        var authEx = new HttpRequestException("SSL error", new AuthenticationException("Remote certificate is invalid"));
        var handlerMock = CreateHandlerMock((req, ct) =>
            Task.FromException<HttpResponseMessage>(authEx));

        var (svc, _) = CreateServiceWithMockFactory(handlerMock, HttpClientNames.Updater);

        var (update, error) = await svc.GetLatestReleaseAsync(default);

        Assert.Null(update);
        Assert.Contains("защищённое соединение", error);
    }

    // ── Helpers ──

    /// <summary>Создаёт mock HttpMessageHandler с заданной логикой SendAsync.</summary>
    private static Mock<HttpMessageHandler> CreateHandlerMock(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        var mock = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(sendAsync);
        return mock;
    }

    /// <summary>Создаёт UpdaterService с mocked IHttpClientFactory.
    /// factoryMock возвращается для дополнительных проверок.</summary>
    private static (UpdaterService svc, Mock<IHttpClientFactory> factoryMock) CreateServiceWithMockFactory(
        Mock<HttpMessageHandler> handlerMock, string clientName)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factoryMock
            .Setup(f => f.CreateClient(clientName))
            .Returns(httpClient);
        return (new UpdaterService(factoryMock.Object), factoryMock);
    }
}
