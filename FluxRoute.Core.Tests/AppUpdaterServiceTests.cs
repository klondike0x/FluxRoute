using System.Net;
using System.Text;
using System.Threading;
using FluxRoute.Updater.Services;
using Moq;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для гибридного механизма проверки обновлений AppUpdaterService:
/// GitHub REST API (первичный) → Atom-лента (fallback при 403/сетевой ошибке).
/// </summary>
public sealed class AppUpdaterServiceTests
{
    private const string CurrentVersion = "1.5.3";

    /// <summary>Создаёт фабрику с кастомным обработчиком, маршрутизирующим по URL.</summary>
    private static IHttpClientFactory CreateFactory(Func<Uri, Task<HttpResponseMessage>> handler)
    {
        var handlerInstance = new TestHttpMessageHandler(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerInstance));
        return factoryMock.Object;
    }

    // ── GitHub API успешно отвечает ──

    [Fact]
    public async Task CheckForAppUpdateAsync_GitHubApiReturnsNewer_ReturnsUpdate()
    {
        var newerVersion = "2.0.0";
        var factory = CreateFactory(uri =>
        {
            if (uri.AbsoluteUri.Contains("api.github.com"))
                return Task.FromResult(MakeJsonResponse(HttpStatusCode.OK, $$"""{"tag_name":"{{newerVersion}}"}"""));
            return Task.FromResult(MakeAtomResponse(newerVersion));
        });

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(error);
        Assert.NotNull(update);
        Assert.Equal(newerVersion, update.Version);
    }

    [Fact]
    public async Task CheckForAppUpdateAsync_GitHubApiReturnsCurrent_ReturnsNull()
    {
        var factory = CreateFactory(uri =>
            Task.FromResult(MakeJsonResponse(HttpStatusCode.OK, $$"""{"tag_name":"{{CurrentVersion}}"}""")));

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(error);
        Assert.Null(update);
    }

    // ── GitHub API 403 → Atom fallback ──

    [Fact]
    public async Task CheckForAppUpdateAsync_GitHubApi403_FallsBackToAtom()
    {
        var factory = CreateFactory(uri =>
        {
            if (!uri.AbsoluteUri.Contains("api.github.com"))
                return Task.FromResult(MakeAtomResponse("2.0.0"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(error);
        Assert.NotNull(update);
        Assert.Equal("2.0.0", update.Version);
    }

    // ── GitHub API сетевая ошибка → Atom fallback ──

    [Fact]
    public async Task CheckForAppUpdateAsync_GitHubApiNetworkError_FallsBackToAtom()
    {
        var factory = CreateFactory(uri =>
        {
            if (!uri.AbsoluteUri.Contains("api.github.com"))
                return Task.FromResult(MakeAtomResponse("2.0.0"));

            throw new HttpRequestException("Network error");
        });

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(error);
        Assert.NotNull(update);
        Assert.Equal("2.0.0", update.Version);
    }

    // ── И GitHub API, и Atom недоступны ──

    [Fact]
    public async Task CheckForAppUpdateAsync_BothSourcesFail_ReturnsError()
    {
        var factory = CreateFactory(uri => throw new HttpRequestException("Network error"));

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(update);
        Assert.NotNull(error);
        Assert.Contains("Atom", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Кэширование ──

    [Fact]
    public async Task CheckForAppUpdateAsync_CacheHit_NoHttpRequest()
    {
        var callCount = 0;
        var factory = CreateFactory(uri =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(MakeJsonResponse(HttpStatusCode.OK, """{"tag_name":"2.0.0"}"""));
        });

        var svc = new TestableAppUpdaterService(factory, CurrentVersion);

        var (update1, _) = await svc.CheckForAppUpdateAsync();
        Assert.NotNull(update1);
        Assert.Equal(1, callCount);

        var (update2, _) = await svc.CheckForAppUpdateAsync();
        Assert.NotNull(update2);
        Assert.Equal(1, callCount); // не должно быть нового запроса
    }

    // ── Валидация тега semver ──

    [Theory]
    [InlineData("v1.5.4")]
    [InlineData("1.5.4")]
    [InlineData("2.0.0")]
    public async Task CheckForAppUpdateAsync_ValidSemVerTags_Works(string tag)
    {
        var factory = CreateFactory(uri =>
        {
            if (uri.AbsoluteUri.Contains("api.github.com"))
                return Task.FromResult(MakeJsonResponse(HttpStatusCode.OK, $$"""{"tag_name":"{{tag}}"}"""));
            return Task.FromResult(MakeAtomResponse(tag));
        });

        var svc = new TestableAppUpdaterService(factory, "1.0.0");
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(error);
        Assert.NotNull(update);
    }

    [Theory]
    [InlineData("v1.5.4-fork")]
    [InlineData("v1.5.4-AI")]
    [InlineData("v1.5.4-custom")]
    [InlineData("invalid")]
    public async Task CheckForAppUpdateAsync_InvalidTags_Rejected(string tag)
    {
        var factory = CreateFactory(uri =>
        {
            if (uri.AbsoluteUri.Contains("api.github.com"))
                return Task.FromResult(MakeJsonResponse(HttpStatusCode.OK, $$"""{"tag_name":"{{tag}}"}"""));
            return Task.FromResult(MakeAtomResponse(tag));
        });

        var svc = new TestableAppUpdaterService(factory, "1.0.0");
        var (update, error) = await svc.CheckForAppUpdateAsync();

        Assert.Null(update);
        Assert.NotNull(error);
    }

    // ── Helper methods ──

    private static HttpResponseMessage MakeJsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage MakeAtomResponse(string tagName)
    {
        var xml = $"""<?xml version="1.0"?><feed><entry><id>tag:github.com,2008:Repository/123456789/{tagName}</id></entry></feed>""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/atom+xml")
        };
    }

    /// <summary>
    /// Кастомный HttpMessageHandler, вызывающий переданный Func для каждого запроса.
    /// </summary>
    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<Uri, Task<HttpResponseMessage>> _handler;
        public TestHttpMessageHandler(Func<Uri, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _handler(request.RequestUri!);
    }

    /// <summary>
    /// Testable subclass that overrides GetCurrentVersion() to return a fixed value.
    /// </summary>
    private sealed class TestableAppUpdaterService : AppUpdaterService
    {
        private readonly string _fakeVersion;

        public TestableAppUpdaterService(IHttpClientFactory httpClientFactory, string fakeVersion)
            : base(httpClientFactory)
        {
            _fakeVersion = fakeVersion;
        }

        public override string GetCurrentVersion() => _fakeVersion;
    }
}
