using System.Diagnostics;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class WarpEngine : IDpiEngine
{
    public DpiEngineType EngineType => DpiEngineType.Warp;
    public string DisplayName => "Warp (warp-plus)";
    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
    public EngineProcessInfo? ProcessInfo { get; private set; }

    private Process? _process;
    private readonly string _engineDir;
    private readonly object _gate = new();
    private bool _disposed;

    public event EventHandler<EngineStatus>? StatusChanged;

    public WarpEngine(string engineDir)
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
            var executable = FindWarpExecutable();
            if (executable is null)
            {
                Status = EngineStatus.Failed;
                NotifyStatus();
                return false;
            }

            var args = BuildWarpArgs(profile);

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? _engineDir,
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
                _process.Id, "warp-plus.exe", EngineStatus.Running,
                DateTimeOffset.Now, 8086); // warp-plus default bind port

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

    private string? FindWarpExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(_engineDir, "warp", "warp-plus.exe"),
            Path.Combine(_engineDir, "warp-plus.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<string> BuildWarpArgs(EngineProfile p)
    {
        var list = new List<string>();

        // Basic flags for warp-plus
        list.Add("-b");
        list.Add("127.0.0.1:8086"); // Default bind for internal proxy use

        if (!string.IsNullOrWhiteSpace(p.WarpConfig))
        {
            list.Add("--wgconf");
            list.Add(p.WarpConfig);
        }

        if (p.MTU.HasValue)
        {
            list.Add("--mtu");
            list.Add(p.MTU.Value.ToString());
        }

        if (p.GoolEnabled)
        {
            list.Add("--gool");
        }

        if (p.PsiphonEnabled)
        {
            list.Add("--cfon");
            if (!string.IsNullOrWhiteSpace(p.PsiphonCountry))
            {
                list.Add("--country");
                list.Add(p.PsiphonCountry);
            }
        }

        if (p.ScanEnabled)
        {
            list.Add("--scan");
        }

        if (!string.IsNullOrWhiteSpace(p.Reserved))
        {
            list.Add("--reserved");
            list.Add(p.Reserved);
        }

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
                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            process.Dispose();
        }
        catch
        {
        }

        try
        {
            foreach (var p in Process.GetProcessesByName("warp-plus"))
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
