using System.Net.NetworkInformation;

namespace FluxRoute.Core.Services;

public readonly record struct NetworkTrafficCounters(long BytesReceived, long BytesSent);

public interface INetworkTrafficCounterSource
{
    bool TryRead(out NetworkTrafficCounters counters);
}

public sealed class NetworkInterfaceTrafficCounterSource : INetworkTrafficCounterSource
{
    public bool TryRead(out NetworkTrafficCounters counters)
    {
        long received = 0;
        long sent = 0;
        var hasActiveInterface = false;

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up
                    || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                try
                {
                    var statistics = networkInterface.GetIPv4Statistics();
                    received = checked(received + statistics.BytesReceived);
                    sent = checked(sent + statistics.BytesSent);
                    hasActiveInterface = true;
                }
                catch (NetworkInformationException)
                {
                    // Отдельный виртуальный адаптер не должен останавливать общий мониторинг.
                }
            }
        }
        catch (NetworkInformationException)
        {
            counters = default;
            return false;
        }
        catch (OverflowException)
        {
            counters = default;
            return false;
        }

        counters = new NetworkTrafficCounters(received, sent);
        return hasActiveInterface;
    }
}

public sealed class NetworkTrafficMonitor : INetworkTrafficMonitor
{
    private readonly INetworkTrafficCounterSource _counterSource;
    private readonly TimeProvider _timeProvider;
    private NetworkTrafficCounters? _previousCounters;
    private DateTimeOffset _previousTimestamp;

    public NetworkTrafficMonitor(
        INetworkTrafficCounterSource counterSource,
        TimeProvider? timeProvider = null)
    {
        _counterSource = counterSource ?? throw new ArgumentNullException(nameof(counterSource));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public NetworkTrafficRate Sample()
    {
        if (!_counterSource.TryRead(out var current))
        {
            _previousCounters = null;
            return new NetworkTrafficRate(0, 0, false);
        }

        var now = _timeProvider.GetUtcNow();
        if (_previousCounters is not { } previous)
        {
            _previousCounters = current;
            _previousTimestamp = now;
            return new NetworkTrafficRate(0, 0, true);
        }

        var elapsedSeconds = (now - _previousTimestamp).TotalSeconds;
        _previousCounters = current;
        _previousTimestamp = now;

        if (elapsedSeconds <= 0)
            return new NetworkTrafficRate(0, 0, true);

        var receivedDelta = Math.Max(0, current.BytesReceived - previous.BytesReceived);
        var sentDelta = Math.Max(0, current.BytesSent - previous.BytesSent);

        return new NetworkTrafficRate(
            receivedDelta / elapsedSeconds,
            sentDelta / elapsedSeconds,
            true);
    }
}

public static class TrafficSpeedFormatter
{
    public static string Format(double bytesPerSecond)
    {
        var safeValue = double.IsFinite(bytesPerSecond) ? Math.Max(0, bytesPerSecond) : 0;
        return safeValue switch
        {
            >= 1024 * 1024 * 1024 => $"{safeValue / (1024 * 1024 * 1024):0.#} ГБ/с",
            >= 1024 * 1024 => $"{safeValue / (1024 * 1024):0.#} МБ/с",
            >= 1024 => $"{safeValue / 1024:0.#} КБ/с",
            _ => $"{safeValue:0} Б/с"
        };
    }
}
