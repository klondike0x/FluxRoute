using System.Diagnostics;
using System.Globalization;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class ByeDpiEngine : IDpiEngine
{
    public DpiEngineType EngineType => DpiEngineType.ByeDpi;
    public string DisplayName => "ByeDPI (ciadpi)";
    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
    public EngineProcessInfo? ProcessInfo { get; private set; }

    private Process? _process;
    private readonly string _engineDir;
    private readonly object _gate = new();
    private bool _disposed;

    public event EventHandler<EngineStatus>? StatusChanged;

    public ByeDpiEngine(string engineDir)
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
            var executable = FindByDpiExecutable();
            if (executable is null)
            {
                Status = EngineStatus.Failed;
                NotifyStatus();
                return false;
            }

            var args = BuildCliArgs(profile);

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = false, // Отключаем перенаправление, так как оно не вычитывается
                RedirectStandardError = false,  // и может приводить к зависанию ciadpi при заполнении буфера
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
                _process.Id, "ciadpi.exe", EngineStatus.Running,
                DateTimeOffset.Now, profile.SocksPort);

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

    public static IReadOnlyList<string> BuildCliArgs(EngineProfile p)
    {
        var list = new List<string>();
        list.Add("-p");
        list.Add(p.SocksPort.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(p.SplitPos))
        {
            list.Add("--split");
            list.Add(p.SplitPos);
        }

        if (!string.IsNullOrWhiteSpace(p.DisorderPos))
        {
            list.Add("--disorder");
            list.Add(p.DisorderPos);
        }

        if (!string.IsNullOrWhiteSpace(p.FakePos))
        {
            list.Add("--fake");
            list.Add(p.FakePos);
        }

        if (!string.IsNullOrWhiteSpace(p.OobPos))
        {
            list.Add("--oob");
            list.Add(p.OobPos);
        }

        if (!string.IsNullOrWhiteSpace(p.DisoobPos))
        {
            list.Add("--disoob");
            list.Add(p.DisoobPos);
        }

        if (!string.IsNullOrWhiteSpace(p.TlsrecPos))
        {
            list.Add("--tlsrec");
            list.Add(p.TlsrecPos);
        }

        if (p.FakeTtl is not null)
        {
            list.Add("--ttl");
            list.Add(p.FakeTtl.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (p.Md5sig == true)
            list.Add("--md5sig");

        if (!string.IsNullOrWhiteSpace(p.FakeTlsMod))
        {
            list.Add("--fake-tls-mod");
            list.Add(p.FakeTlsMod);
        }

        if (!string.IsNullOrWhiteSpace(p.FakeSni))
        {
            list.Add("--fake-sni");
            list.Add(p.FakeSni);
        }

        if (!string.IsNullOrWhiteSpace(p.FakeData))
        {
            list.Add("--fake-data");
            list.Add(p.FakeData);
        }

        if (!string.IsNullOrWhiteSpace(p.ModHttp))
        {
            list.Add("--mod-http");
            list.Add(p.ModHttp);
        }

        if (p.Tlsminor is not null)
        {
            list.Add("--tlsminor");
            list.Add(p.Tlsminor.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(p.Auto))
        {
            list.Add("--auto");
            list.Add(p.Auto);
        }

        if (p.Timeout is not null)
        {
            list.Add("--timeout");
            list.Add(p.Timeout.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (p.AutoMode is not null)
        {
            list.Add("--auto-mode");
            list.Add(p.AutoMode.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(p.Hosts))
        {
            list.Add("--hosts");
            list.Add(p.Hosts);
        }

        if (p.CacheTtl is not null)
        {
            list.Add("--cache-ttl");
            list.Add(p.CacheTtl.Value.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var x in p.ExtraArgs)
        {
            if (!string.IsNullOrWhiteSpace(x))
                list.Add(x);
        }

        return list;
    }

    private string? FindByDpiExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(_engineDir, "byedpi", "ciadpi.exe"),
            Path.Combine(_engineDir, "ciadpi.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
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
            foreach (var p in Process.GetProcessesByName("ciadpi"))
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
