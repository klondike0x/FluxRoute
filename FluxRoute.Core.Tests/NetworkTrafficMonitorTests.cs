using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

public sealed class NetworkTrafficMonitorTests
{
    [Fact]
    public void Sample_AfterOneSecond_ReturnsReceivedAndSentRates()
    {
        var source = new SequenceCounterSource(
            new NetworkTrafficCounters(1_000, 2_000),
            new NetworkTrafficCounters(3_500, 3_000));
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var monitor = new NetworkTrafficMonitor(source, time);

        _ = monitor.Sample();
        time.Advance(TimeSpan.FromSeconds(1));
        var rate = monitor.Sample();

        Assert.True(rate.IsAvailable);
        Assert.Equal(2_500, rate.DownloadBytesPerSecond);
        Assert.Equal(1_000, rate.UploadBytesPerSecond);
    }

    [Fact]
    public void Sample_WhenCountersUnavailable_ReturnsUnavailableRate()
    {
        var monitor = new NetworkTrafficMonitor(
            new UnavailableCounterSource(),
            new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        var rate = monitor.Sample();

        Assert.False(rate.IsAvailable);
        Assert.Equal(0, rate.DownloadBytesPerSecond);
        Assert.Equal(0, rate.UploadBytesPerSecond);
    }

    [Fact]
    public void Sample_WhenCountersReset_DoesNotProduceNegativeRate()
    {
        var source = new SequenceCounterSource(
            new NetworkTrafficCounters(10_000, 10_000),
            new NetworkTrafficCounters(100, 200));
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var monitor = new NetworkTrafficMonitor(source, time);

        _ = monitor.Sample();
        time.Advance(TimeSpan.FromSeconds(1));
        var rate = monitor.Sample();

        Assert.Equal(0, rate.DownloadBytesPerSecond);
        Assert.Equal(0, rate.UploadBytesPerSecond);
    }

    [Theory]
    [InlineData(0, "0 Б/с")]
    [InlineData(1_536, "1,5 КБ/с")]
    [InlineData(2_621_440, "2,5 МБ/с")]
    public void Format_UsesReadableBinaryUnits(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, TrafficSpeedFormatter.Format(bytesPerSecond));
    }

    [Fact]
    public void CreateDisplay_WhenProtectionIsStopped_HidesSystemTraffic()
    {
        var display = NetworkTrafficDisplay.Create(
            protectionIsRunning: false,
            new NetworkTrafficRate(4_194_304, 1_048_576, true));

        Assert.Equal("0 Б/с", display.Download);
        Assert.Equal("0 Б/с", display.Upload);
    }

    [Fact]
    public void CreateDisplay_WhenProtectionIsRunning_ShowsMeasuredTraffic()
    {
        var display = NetworkTrafficDisplay.Create(
            protectionIsRunning: true,
            new NetworkTrafficRate(4_194_304, 1_048_576, true));

        Assert.Equal("4 МБ/с", display.Download);
        Assert.Equal("1 МБ/с", display.Upload);
    }

    private sealed class SequenceCounterSource(params NetworkTrafficCounters[] values)
        : INetworkTrafficCounterSource
    {
        private int _index;

        public bool TryRead(out NetworkTrafficCounters counters)
        {
            counters = values[Math.Min(_index, values.Length - 1)];
            _index++;
            return true;
        }
    }

    private sealed class UnavailableCounterSource : INetworkTrafficCounterSource
    {
        public bool TryRead(out NetworkTrafficCounters counters)
        {
            counters = default;
            return false;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan interval) => _now += interval;
    }
}
