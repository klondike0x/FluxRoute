using System.Net.NetworkInformation;
using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class NetworkChangeWatcher : IDisposable
{
    private readonly NetworkFingerprintProvider _fingerprints;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();

    private Timer? _debounceTimer;
    private NetworkFingerprint? _lastEmitted;
    private int _generation;

    /// <summary>
    /// Номер изменения сети: увеличивается каждый раз, когда зафиксированный отпечаток сменился.
    /// Нужен длинным операциям (полный скан): сравнение отпечатков на концах не замечает
    /// кратковременный разрыв, когда сеть ушла и вернулась с тем же хэшем, а часть замеров сделана
    /// без сети или на другой сети (Codex P2, ревью pullrequestreview-5191837234).
    /// </summary>
    public int Generation => Volatile.Read(ref _generation);

    public event EventHandler<(NetworkFingerprint OldFp, NetworkFingerprint NewFp)>? NetworkChanged;

    public NetworkChangeWatcher(NetworkFingerprintProvider fingerprints)
    {
        _fingerprints = fingerprints;
        NetworkChange.NetworkAddressChanged += OnNetworkChange;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailability;
        _lastEmitted = _fingerprints.Capture();
    }

    private void OnNetworkChange(object? sender, EventArgs e) => ScheduleEmit();

    private void OnNetworkAvailability(object? sender, NetworkAvailabilityEventArgs e) => ScheduleEmit();

    private void ScheduleEmit()
    {
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try
                {
                    var next = _fingerprints.Capture();
                    NetworkFingerprint? oldSnap;
                    lock (_gate)
                    {
                        oldSnap = _lastEmitted;
                        if (oldSnap?.Hash == next.Hash)
                            return;
                        _lastEmitted = next;
                        Interlocked.Increment(ref _generation);
                    }

                    if (oldSnap is not null)
                        NetworkChanged?.Invoke(this, (oldSnap, next));
                }
                catch
                {
                }
            }, null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public NetworkFingerprint GetLastFingerprint()
    {
        lock (_gate)
        {
            return _lastEmitted ?? _fingerprints.Capture();
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChange;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailability;
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
