using System.Diagnostics;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class ZapretEngine : IDpiEngine
{
    public DpiEngineType EngineType => DpiEngineType.Zapret;
    public string DisplayName => "Zapret (winws.exe)";
    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
    public EngineProcessInfo? ProcessInfo { get; private set; }

    private Process? _process;
    private readonly string _engineDir;
    private readonly object _gate = new();
    private bool _disposed;

    public event EventHandler<EngineStatus>? StatusChanged;

    public ZapretEngine(string engineDir)
    {
        _engineDir = engineDir;
    }

    public async Task<bool> StartAsync(EngineProfile profile, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Status == EngineStatus.Running || Status == EngineStatus.Starting)
                return true;
            Status = EngineStatus.Starting;
        }

        try
        {
            ProfileBatLauncher.PrepareRuntime(_engineDir);

            var args = BuildWinwsArgs(profile);
            var binDir = Path.Combine(_engineDir, "bin");
            var executable = Path.Combine(binDir, "winws.exe");

            if (!File.Exists(executable))
            {
                Status = EngineStatus.Failed;
                NotifyStatus();
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = false, // Отключаем перенаправление, так как оно не вычитывается
                RedirectStandardError = false,  // и может приводить к зависанию winws при заполнении буфера
                CreateNoWindow = true,
                WorkingDirectory = binDir,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            _process = new Process { StartInfo = psi };
            _process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    Status = EngineStatus.Crashed;
                    _process = null;
                    ProcessInfo = null;
                }
                NotifyStatus();
            };
            _process.EnableRaisingEvents = true;

            if (!_process.Start())
            {
                Status = EngineStatus.Failed;
                NotifyStatus();
                return false;
            }

            ProcessInfo = new EngineProcessInfo(
                _process.Id, "winws.exe", EngineStatus.Running,
                DateTimeOffset.Now, null);

            Status = EngineStatus.Running;
            NotifyStatus();
            return true;
        }
        catch
        {
            Status = EngineStatus.Failed;
            NotifyStatus();
            return false;
        }
    }

    public Task<bool> StopAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            TryKillProcess(_process);
            _process = null;
            Status = EngineStatus.Stopped;
            ProcessInfo = null;
        }
        NotifyStatus();
        return Task.FromResult(true);
    }

    public Task<EngineStatus> ProbeStatusAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_process is not null && _process.HasExited)
            {
                Status = EngineStatus.Crashed;
                _process = null;
                ProcessInfo = null;
            }
            return Task.FromResult(Status);
        }
    }

    private static IReadOnlyList<string> BuildWinwsArgs(EngineProfile p)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.FilterTcp)) { list.Add("--filter-tcp"); list.Add(p.FilterTcp); }
        if (!string.IsNullOrWhiteSpace(p.FilterUdp)) { list.Add("--filter-udp"); list.Add(p.FilterUdp); }
        list.Add("--dpi-desync"); list.Add(p.DesyncMode);

        if (!string.IsNullOrWhiteSpace(p.SplitPos))
        {
            list.Add("--dpi-desync-split-pos");
            list.Add(p.SplitPos);
        }

        if (!string.IsNullOrWhiteSpace(p.FakeTlsMod))
        {
            list.Add("--dpi-desync-fake-tls-mod");
            list.Add(p.FakeTlsMod);
        }

        if (p.FakeTtl is not null)
        {
            list.Add("--dpi-desync-ttl");
            list.Add(p.FakeTtl.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (p.RepeatCount is not null)
        {
            list.Add("--dpi-desync-repeats");
            list.Add(p.RepeatCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(p.DesyncAnyProtocol))
        {
            list.Add("--dpi-desync-any-protocol");
            list.Add(p.DesyncAnyProtocol);
        }

        if (!string.IsNullOrWhiteSpace(p.DesyncFooling))
        {
            list.Add("--dpi-desync-fooling");
            list.Add(p.DesyncFooling);
        }

        if (!string.IsNullOrWhiteSpace(p.FakeResend))
        {
            list.Add("--dpi-desync-fake-resend");
            list.Add(p.FakeResend);
        }

        if (!string.IsNullOrWhiteSpace(p.Hostlist))
        {
            list.Add("--hostlist");
            list.Add(p.Hostlist);
        }

        list.Add("--new");
        foreach (var x in p.ExtraArgs)
        {
            if (!string.IsNullOrWhiteSpace(x))
                list.Add(x);
        }

        return list;
    }

    private void NotifyStatus()
    {
        StatusChanged?.Invoke(this, Status);
    }

    private static void TryKillProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                // Ждем завершения, но не слишком долго в синхронном вызове
                if (!process.WaitForExit(3000))
                {
                    // Если не завершился за 3с, пробуем убить еще раз жестко
                    process.Kill(entireProcessTree: true);
                }
            }
            process.Dispose();
        }
        catch
        {
        }

        // Дополнительная очистка всех winws.exe для надежности
        try
        {
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }
}
