using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Http.Headers;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public interface IDohProviderService
{
    IReadOnlyList<DohProvider> Providers { get; }

    Task<DohProviderTestResult> TestProviderAsync(
        DohProvider provider,
        int attempts = 4,
        CancellationToken ct = default);

    Task<IReadOnlyList<DohProviderTestResult>> TestAllAsync(
        int attempts = 4,
        CancellationToken ct = default);
}

/// <summary>Проверяет публичные DoH-провайдеры реальными DNS wire-format запросами.</summary>
public sealed class DohProviderService : IDohProviderService
{
    private static readonly string[] ProbeDomains =
    [
        "www.microsoft.com",
        "chatgpt.com",
        "gemini.google.com"
    ];
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DohProviderService> _logger;

    public DohProviderService(
        IHttpClientFactory httpClientFactory,
        ILogger<DohProviderService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<DohProvider> Providers { get; } =
    [
        new("cloudflare", "Cloudflare", new Uri("https://one.one.one.one/dns-query"),
            ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"], true),
        new("google", "Google", new Uri("https://dns.google/dns-query"),
            ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"], true),
        new("quad9", "Quad9", new Uri("https://dns.quad9.net/dns-query"),
            ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"], true),
        new("adguard", "AdGuard DNS", new Uri("https://dns.adguard-dns.com/dns-query"),
            ["94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"], true),
        new("comss", "COMSS DNS", new Uri("https://dns.comss.one/dns-query"),
            ["83.220.169.155", "212.109.195.93"], true),
        new("malw", "dns.malw.link", new Uri("https://dns.malw.link/dns-query"),
            ["95.216.204.218", "80.253.249.40", "2a01:4f9:c014:6dac::1", "2a12:bec4:1460:5b7::2"], true),
        new("xbox-dns", "Xbox DNS", new Uri("https://xbox-dns.ru/dns-query"),
            ["111.88.96.50", "111.88.96.51", "2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"], true)
    ];

    public async Task<DohProviderTestResult> TestProviderAsync(
        DohProvider provider,
        int attempts = 4,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (attempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Количество попыток должно быть от 1 до 10.");
        }

        var latencies = new List<double>(attempts);
        string? lastError = null;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Doh);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            var probeDomain = ProbeDomains[attempt % ProbeDomains.Length];
            var query = CreateDnsQuery(transactionId, probeDomain);
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.Template)
            {
                Content = new ByteArrayContent(query)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode && IsValidDnsResponse(body, transactionId))
                {
                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                    continue;
                }

                lastError = $"HTTP {(int)response.StatusCode} или некорректный DNS-ответ";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                stopwatch.Stop();
                lastError = ex.Message;
                _logger.LogWarning(ex, "Проверка DoH-провайдера {Provider} завершилась ошибкой", provider.Name);
            }
        }

        var successRate = latencies.Count / (double)attempts;
        return new DohProviderTestResult(
            provider,
            latencies.Count == 0 ? null : latencies.Average(),
            successRate,
            attempts,
            latencies.Count == attempts ? null : lastError ?? "Часть запросов не выполнена",
            0);
    }

    public async Task<IReadOnlyList<DohProviderTestResult>> TestAllAsync(
        int attempts = 4,
        CancellationToken ct = default)
    {
        var tasks = Providers.Select(provider => TestProviderAsync(provider, attempts, ct));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static byte[] CreateDnsQuery(ushort transactionId, string domain)
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);
        stream.Write(header);

        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        Span<byte> question = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(question, 1);
        BinaryPrimitives.WriteUInt16BigEndian(question[2..], 1);
        stream.Write(question);
        return stream.ToArray();
    }

    private static bool IsValidDnsResponse(byte[] response, ushort transactionId)
    {
        if (response.Length < 12)
        {
            return false;
        }

        var responseId = BinaryPrimitives.ReadUInt16BigEndian(response);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2));
        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(4));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6));
        var isResponse = (flags & 0x8000) != 0;
        var responseCode = flags & 0x000F;
        return responseId == transactionId
            && isResponse
            && responseCode == 0
            && questionCount == 1
            && answerCount > 0;
    }
}
