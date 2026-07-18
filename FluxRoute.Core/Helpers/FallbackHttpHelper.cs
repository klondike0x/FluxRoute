using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Helpers;

/// <summary>
/// Хелпер для скачивания ресурсов через цепочку зеркал.
/// Пробует URL по порядку, логирует успех/провал каждого.
/// Используется для engine, ipset, hosts, tg-proxy.
/// </summary>
public static class FallbackHttpHelper
{
    /// <summary>
    /// Пытается скачать контент по цепочке URL.
    /// Возвращает контент и индекс успешного URL (0-based).
    /// Если все URL недоступны — возвращает null.
    /// </summary>
    public static async Task<(string? content, int mirrorIndex)?> TryFetchFromMirrorsAsync(
        IReadOnlyList<string> urls,
        HttpClient httpClient,
        Microsoft.Extensions.Logging.ILogger? logger,
        string resourceDescription,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (urls.Count == 0)
        {
            logger?.LogWarning("Нет URL для загрузки {Resource}", resourceDescription);
            return null;
        }

        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            var sourceLabel = i == 0 ? "основной" : $"зеркало #{i}";

            try
            {
                logger?.LogDebug("Попытка {Attempt}/{Total} ({Source}): {Url}",
                    i + 1, urls.Count, sourceLabel, url);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning(
                        "{Resource}: {Source} вернул HTTP {StatusCode} — {Url}",
                        resourceDescription, sourceLabel, (int)response.StatusCode, url);
                    continue;
                }

                var content = await response.Content
                    .ReadAsStringAsync(ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(content))
                {
                    logger?.LogWarning(
                        "{Resource}: {Source} вернул пустой ответ — {Url}",
                        resourceDescription, sourceLabel, url);
                    continue;
                }

                logger?.LogInformation(
                    "{Resource}: загружено через {Source} ({Index}/{Total}) — {Url}",
                    resourceDescription, sourceLabel, i + 1, urls.Count, url);

                return (content, i);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger?.LogDebug("{Resource}: операция отменена пользователем", resourceDescription);
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "{Resource}: {Source} недоступен — {Url}",
                    resourceDescription, sourceLabel, url);
                // Продолжаем к следующему зеркалу
            }
        }

        logger?.LogError(
            "{Resource}: все источники ({Count}) недоступны",
            resourceDescription, urls.Count);
        return null;
    }

    /// <summary>
    /// Строит цепочку URL: основной → указанные зеркала → fallback-зеркала.
    /// Дубликаты исключаются (регистронезависимо).
    /// </summary>
    public static List<string> BuildUrlChain(
        string primaryUrl,
        IReadOnlyList<string>? mirrorUrls,
        params string[] fallbackUrls)
    {
        var chain = new List<string> { primaryUrl };

        if (mirrorUrls is { Count: > 0 })
        {
            foreach (var mirror in mirrorUrls)
            {
                if (!string.IsNullOrWhiteSpace(mirror) && !chain.Contains(mirror, StringComparer.OrdinalIgnoreCase))
                    chain.Add(mirror);
            }
        }

        foreach (var fb in fallbackUrls)
        {
            if (!string.IsNullOrWhiteSpace(fb) && !chain.Contains(fb, StringComparer.OrdinalIgnoreCase))
                chain.Add(fb);
        }

        return chain;
    }
}
