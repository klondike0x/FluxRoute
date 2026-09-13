using FluxRoute.AI.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Счётчик изменений сети обязан срабатывать на каждый сигнал о смене сети, а не по итогу дебаунса:
/// разрыв, при котором сеть ушла и вернулась к прежнему отпечатку внутри окна дебаунса, дебаунс не
/// переживает (отпечатки на концах совпадают), и пропущенный счётчик вернул бы длинному скану импорт
/// замеров, снятых без сети (Codex P2, ревью pullrequestreview-5191875744).
/// </summary>
public sealed class NetworkChangeWatcherTests
{
    [Fact]
    public void RegisterNetworkChangeSignal_IncrementsGenerationBeforeDebounce()
    {
        using var watcher = new NetworkChangeWatcher(new NetworkFingerprintProvider());
        var before = watcher.Generation;

        // Именно так о смене сети узнаёт сам watcher: NetworkAddressChanged/NetworkAvailabilityChanged.
        watcher.RegisterNetworkChangeSignal();

        // Счётчик двигается сразу, не дожидаясь трёхсекундного дебаунса.
        Assert.Equal(before + 1, watcher.Generation);

        watcher.RegisterNetworkChangeSignal();

        Assert.Equal(before + 2, watcher.Generation);
    }
}
