using System.Net;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohProviderServiceTests
{
    [Fact]
    public void Providers_IncludeRequestedRussianDohServices()
    {
        // Arrange
        var service = CreateService(new DnsResponseHandler(HttpStatusCode.OK, validDnsResponse: true));

        // Act
        var providers = service.Providers.ToDictionary(provider => provider.Id);

        // Assert
        Assert.Equal(
            ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"],
            providers["cloudflare"].DnsAddresses);
        Assert.Equal(
            ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"],
            providers["google"].DnsAddresses);
        Assert.Equal(
            ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"],
            providers["quad9"].DnsAddresses);
        Assert.Equal(
            ["94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"],
            providers["adguard"].DnsAddresses);
        Assert.Equal("https://dns.comss.one/dns-query", providers["comss"].Template.AbsoluteUri);
        Assert.Equal(["83.220.169.155", "212.109.195.93"], providers["comss"].DnsAddresses);
        Assert.Equal("https://dns.malw.link/dns-query", providers["malw"].Template.AbsoluteUri);
        Assert.Equal(
            ["95.216.204.218", "80.253.249.40", "2a01:4f9:c014:6dac::1", "2a12:bec4:1460:5b7::2"],
            providers["malw"].DnsAddresses);
        Assert.Equal("https://xbox-dns.ru/dns-query", providers["xbox-dns"].Template.AbsoluteUri);
        Assert.Equal(
            ["111.88.96.50", "111.88.96.51", "2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"],
            providers["xbox-dns"].DnsAddresses);
    }

    [Fact]
    public async Task TestProviderAsync_WhenAllDnsQueriesSucceed_ReturnsFullSuccessRate()
    {
        // Arrange
        var handler = new DnsResponseHandler(HttpStatusCode.OK, validDnsResponse: true);
        var service = CreateService(handler);
        var provider = CreateProvider();

        // Act
        var result = await service.TestProviderAsync(provider, attempts: 3);

        // Assert
        Assert.Equal(1d, result.SuccessRate);
        Assert.NotNull(result.AverageLatencyMs);
        Assert.Null(result.Error);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task TestProviderAsync_WhenEndpointReturnsError_ReturnsUnavailableResult()
    {
        // Arrange
        var handler = new DnsResponseHandler(HttpStatusCode.ServiceUnavailable, validDnsResponse: false);
        var service = CreateService(handler);

        // Act
        var result = await service.TestProviderAsync(CreateProvider(), attempts: 2);

        // Assert
        Assert.Equal(0d, result.SuccessRate);
        Assert.Null(result.AverageLatencyMs);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task TestProviderAsync_WhenAttemptsIsOutsideRange_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var service = CreateService(new DnsResponseHandler(HttpStatusCode.OK, validDnsResponse: true));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.TestProviderAsync(CreateProvider(), attempts: 0));
    }

    private static DohProviderService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(HttpClientNames.Doh)).Returns(client);
        var logger = new Mock<ILogger<DohProviderService>>();
        return new DohProviderService(factory.Object, logger.Object);
    }

    private static DohProvider CreateProvider() => new(
        "test",
        "Test",
        new Uri("https://dns.example/dns-query"),
        ["1.1.1.1"],
        true);

    private sealed class DnsResponseHandler(HttpStatusCode statusCode, bool validDnsResponse) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var query = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var response = validDnsResponse ? CreateDnsResponse(query) : [];
            return new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(response)
            };
        }

        private static byte[] CreateDnsResponse(byte[] query)
        {
            var response = new byte[query.Length + 16];
            query.CopyTo(response, 0);
            response[2] = 0x81;
            response[3] = 0x80;
            response[6] = 0;
            response[7] = 1;

            var offset = query.Length;
            response[offset] = 0xC0;
            response[offset + 1] = 0x0C;
            response[offset + 2] = 0;
            response[offset + 3] = 1;
            response[offset + 4] = 0;
            response[offset + 5] = 1;
            response[offset + 10] = 0;
            response[offset + 11] = 4;
            response[offset + 12] = 192;
            response[offset + 13] = 0;
            response[offset + 14] = 2;
            response[offset + 15] = 1;
            return response;
        }
    }
}
