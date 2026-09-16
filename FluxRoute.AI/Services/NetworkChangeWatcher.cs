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
    /// Номер изменения сети: увеличивается на КАЖДЫЙ полученный от системы сигнал о смене сети — сразу,
    /// ДО дебаунса. Именно до: разрыв, при котором сеть ушла и вернулась к прежнему отпечатку внутри
    /// окна дебаунса, не переживает дебаунс (отпечатки на концах совпадают, события нет), и счётчик,
    /// увеличиваемый только по итогу дебаунса, такую смену пропускал бы. Длинному скану этого достаточно:
    /// сравнивая счётчик на концах, он отбрасывает замеры, сделанные во время недоступной или другой
    /// сети (Codex P2, ревью pullrequestreview-5191837234 и pullrequestreview-5191875744).
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

    private void OnNetworkChange(object? sender, EventArgs e) => RegisterNetworkChangeSignal();

    private void OnNetworkAvailability(object? sender, NetworkAvailabilityEventArgs e) => RegisterNetworkChangeSignal();

    /// <summary>
    /// Регистрирует сигнал о смене сети: счётчик увеличивается сразу, а не по итогу дебаунса, — иначе
    /// внутриоконный разрыв с возвратом к прежнему отпечатку стирался бы вместе с дебаунсом
    /// (Codex P2, ревью pullrequestreview-5191875744). Дальше отпечаток пересчитывается с дебаунсом.
    /// </summary>
    internal void RegisterNetworkChangeSignal()
    {
        Interlocked.Increment(ref _generation);
        ScheduleEmit();
    }

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
