using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private readonly IZapret2StatusService? _zapret2StatusService;

    private readonly IZapret2RecoveryService? _zapret2RecoveryService;
    private readonly IZapret2DiagnosticsService? _zapret2DiagnosticsService;
    private readonly DispatcherTimer _zapret2StatusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(8)
    };
    private CancellationTokenSource? _zapret2StatusCts;
    private bool _zapret2UserStopped = true;
    private bool _zapret2StatusTimerAttached;

    public Zapret2StatusSnapshot Zapret2Status =>
        _zapret2StatusService?.Current ?? Zapret2StatusSnapshot.Stopped("Сервис состояния Zapret2 не зарегистрирован.");

    public bool IsZapret2Selected => string.Equals(_selectedComponent, "zapret2", StringComparison.OrdinalIgnoreCase);
    public string Zapret2StatusText => Zapret2Status.Status switch
    {
        ProtectionStatus.Starting => "Запускается…",
        ProtectionStatus.Stopping => "Остановка…",
        ProtectionStatus.Healthy => "Работает",
        ProtectionStatus.Degraded => "Частично работает",
        ProtectionStatus.Error => "Ошибка",
        ProtectionStatus.Repairing => "Восстановление…",
        _ => "Остановлено"
    };
    public string Zapret2EngineText => "Zapret 2 · winws2";
    public string Zapret2ProfileText => Zapret2Status.ActiveProfile;
    public string Zapret2Winws2Text => Zapret2Status.GetCheck("winws2").Detail;
    public string Zapret2WinDivertText => Zapret2Status.GetCheck("windivert").Detail;
    public string Zapret2ConnectionText => Zapret2Status.GetCheck("connection").Detail;
    public string Zapret2TrafficText => Zapret2Status.GetCheck("traffic").Detail;
    public string Zapret2LastCheckText =>
        Zapret2Status.CheckedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    [RelayCommand]
    private async Task CheckZapret2StatusAsync()
    {
        _zapret2UserStopped = false;
        await RefreshZapret2StatusAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RepairZapret2Async()
    {
        if (_zapret2RecoveryService is null)
            return;

        var request = CreateZapret2StartRequest();
        if (request is null)
        {
            AddZapret2Log("Ошибка восстановления: не найден winws2.exe или профиль Lua.");
            return;
        }

        _zapret2UserStopped = false;
        var result = await _zapret2RecoveryService.RecoverAsync(
            new Zapret2RepairRequest(request, AllowFallback: false),
            async ct => { await RefreshZapret2StatusAsync().ConfigureAwait(true); return Zapret2Status; },
            CancellationToken.None).ConfigureAwait(true);
        AddZapret2Log(result.Message);
        await RefreshZapret2StatusAsync().ConfigureAwait(true);
    }

    private async Task StartZapret2Async()
    {
        if (_zapret2StatusService is null)
        {
            AddZapret2Log("Сервис состояния Zapret2 не зарегистрирован.");
            return;
        }

        var request = CreateZapret2StartRequest();
        if (request is null)
        {
            _zapret2UserStopped = false;
            AddZapret2Log("Ошибка запуска: не найден winws2.exe или профиль Lua.");
            await RefreshZapret2StatusAsync().ConfigureAwait(true);
            return;
        }

        _zapret2UserStopped = false;
        var result = await _zapret2StatusService.StartAsync(request, CancellationToken.None)
            .ConfigureAwait(true);
        AddZapret2Log(result.Message);
        await RefreshZapret2StatusAsync().ConfigureAwait(true);
    }

    private async Task StopZapret2Async()
    {
        if (_zapret2StatusService is null)
            return;

        _zapret2UserStopped = true;
        var result = await _zapret2StatusService.StopAsync(CancellationToken.None)
            .ConfigureAwait(true);
        AddZapret2Log(result.Message);
        await RefreshZapret2StatusAsync().ConfigureAwait(true);
    }

    private void InitializeZapret2Status()
    {
        if (!IsZapret2Selected || _zapret2StatusService is null || _zapret2DiagnosticsService is null)
            return;

        InitializeZapret2Profiles();
        if (!_zapret2StatusTimerAttached)
        {
            _zapret2StatusTimer.Tick += OnZapret2StatusTimerTick;
            _zapret2StatusTimerAttached = true;
        }
        _zapret2StatusTimer.Start();
        _ = RefreshZapret2StatusAsync();
    }

    private async void OnZapret2StatusTimerTick(object? sender, EventArgs e)
    {
        await RefreshZapret2StatusAsync().ConfigureAwait(true);
    }
    private async Task RefreshZapret2StatusAsync()
    {
        if (!IsZapret2Selected || _zapret2StatusService is null || _zapret2DiagnosticsService is null)
            return;

        _zapret2StatusCts?.Cancel();
        _zapret2StatusCts?.Dispose();
        _zapret2StatusCts = new CancellationTokenSource();
        var ct = _zapret2StatusCts.Token;

        try
        {
            var request = CreateZapret2RuntimeRequest();
            if (request is null)
            {
                _zapret2UserStopped = false;
                await _zapret2StatusService.CheckAsync(
                    new Zapret2HealthInput(false, false, false, false, false, false, false, false, false, false),
                    "—",
                    null,
                    ct).ConfigureAwait(true);
            }
            else
            {
                var input = await _zapret2DiagnosticsService.CollectAsync(
                    request.EngineDirectory,
                    request.ProfilePath,
                    request.LogPath,
                    request.Targets,
                    _zapret2UserStopped,
                    ct).ConfigureAwait(true);
                await _zapret2StatusService.CheckAsync(
                    input,
                    request.ProfilePath ?? "—",
                    request.LogPath,
                    ct).ConfigureAwait(true);
            }

            NotifyZapret2Properties();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddZapret2Log("Ошибка проверки Zapret2: " + ex.Message);
        }
    }

    private Zapret2RuntimeRequest? CreateZapret2RuntimeRequest()
    {
        var start = CreateZapret2StartRequest();
        if (start is null)
            return null;

        return new Zapret2RuntimeRequest(
            GetZapret2EngineDirectory(),
            start.ExecutablePath,
            start.WorkingDirectory,
            start.Arguments,
            SelectedProfile?.FullPath,
            start.LogPath,
            GetZapret2Targets(),
            _zapret2UserStopped);
    }

    private Zapret2StartRequest? CreateZapret2StartRequest()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedProfile.FullPath))
            return null;

        var executable = GetZapret2ExecutablePath();
        if (!File.Exists(executable))
            return null;

        var profilePath = SelectedProfile.FullPath;
        if (!File.Exists(profilePath))
            return null;

        var arguments = new List<string>();
        var luaDirectory = Path.Combine(GetZapret2EngineDirectory(), "lua");
        AddLuaInitIfPresent(arguments, Path.Combine(luaDirectory, "zapret-lib.lua"));
        AddLuaInitIfPresent(arguments, Path.Combine(luaDirectory, "zapret-antidpi.lua"));
        AddLuaInitIfPresent(arguments, profilePath);

        if (arguments.Count == 0)
            return null;

        return new Zapret2StartRequest(
            executable,
            Path.GetDirectoryName(executable) ?? GetZapret2EngineDirectory(),
            arguments,
            Path.GetFileName(profilePath),
            Path.Combine(GetZapret2EngineDirectory(), "logs", "winws2.log"));
    }

    private static void AddLuaInitIfPresent(List<string> arguments, string path)
    {
        if (File.Exists(path))
            arguments.Add("--lua-init=@" + path);
    }

    private string GetZapret2ExecutablePath()
    {
        var engine = GetZapret2EngineDirectory();
        return Path.Combine(engine, "bin", "winws2.exe");
    }

    private string GetZapret2EngineDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine2"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "zapret2"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine")
        };

        return candidates.FirstOrDefault(path =>
            File.Exists(Path.Combine(path, "bin", "winws2.exe"))
            || Directory.Exists(path))
            ?? candidates[0];
    }

    private void InitializeZapret2Profiles()
    {
        if (!IsZapret2Selected)
            return;

        var engine = GetZapret2EngineDirectory();
        var files = Directory.Exists(engine)
            ? Directory.EnumerateFiles(engine, "*.lua", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Equals("zapret-lib.lua", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals("zapret-antidpi.lua", StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .ToList()
            : new List<string>();

        if (files.Count == 0)
            return;

        Profiles.Clear();
        foreach (var path in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(new ProfileItem
            {
                FileName = Path.GetFileName(path),
                DisplayName = Path.GetFileNameWithoutExtension(path),
                FullPath = path
            });
        }

        if (SelectedProfile is null || !Profiles.Contains(SelectedProfile))
            SelectedProfile = Profiles[0];
    }

    private IReadOnlyList<TargetEntry> GetZapret2Targets()
    {
        var targets = new List<TargetEntry>();
        var selectedSites = new[] { "YouTube", "Discord", "Google" };

        foreach (var site in selectedSites)
        {
            if (ConnectivityChecker.BuiltinSites.TryGetValue(site, out var siteTargets))
                targets.AddRange(siteTargets);
        }

        return targets;
    }

    private void AddZapret2Log(string message)
    {
        Logs.Add("[Zapret2] " + message);
        AddToRecentLogs("[Zapret2] " + message);
    }

    private void NotifyZapret2Properties()
    {
        OnPropertyChanged(nameof(Zapret2Status));
        OnPropertyChanged(nameof(Zapret2StatusText));
        OnPropertyChanged(nameof(Zapret2ProfileText));
        OnPropertyChanged(nameof(Zapret2Winws2Text));
        OnPropertyChanged(nameof(Zapret2WinDivertText));
        OnPropertyChanged(nameof(Zapret2ConnectionText));
        OnPropertyChanged(nameof(Zapret2TrafficText));
        OnPropertyChanged(nameof(Zapret2LastCheckText));
        NotifyEngineSummaryProperties();
    }

    public void DisposeZapret2Status()
    {
        _zapret2StatusCts?.Cancel();
        _zapret2StatusTimer.Stop();
    }
}