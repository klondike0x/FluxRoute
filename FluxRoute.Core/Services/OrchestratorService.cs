using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class OrchestratorEventArgs : EventArgs
{
    public string Message { get; init; } = "";
    public bool IsSwitched { get; init; }
    public string? NewProfile { get; init; }
    public ProfileProbeResult? ProbeResult { get; init; }
}

public sealed class OrchestratorService : IDisposable
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(20);
    public double FailThreshold { get; set; } = 0.5;
    public int RequiredFailuresBeforeSwitch { get; set; } = 2;
    public HashSet<string> EnabledSites { get; set; } = ["YouTube", "Discord", "Google", "Twitch", "Instagram", "Telegram"];

    public bool IsRunning => _cts is not null;
    public bool IsScanning { get; private set; }
    public DateTimeOffset? NextCheckAt { get; private set; }

    private List<(ProfileItem profile, int score, ProfileProbeResult? result)> _rankedProfiles = [];

    private readonly Func<IEnumerable<ProfileItem>> _getProfiles;
    private readonly Func<ProfileItem?> _getActiveProfile;
    private readonly Func<ProfileItem?, Task> _switchProfile;
    private readonly Func<string> _getTargetsPath;
    private readonly Func<string, int, Task> _notifyScoreUpdate;
    private readonly ProfileProbeService _probeService;
    private readonly IConnectivityChecker _connectivity;

    private CancellationTokenSource? _cts;
    private int _consecutiveFailures;

    public event EventHandler<OrchestratorEventArgs>? StatusChanged;

    public OrchestratorService(
        Func<IEnumerable<ProfileItem>> getProfiles,
        Func<ProfileItem?> getActiveProfile,
        Func<ProfileItem?, Task> switchProfile,
        Func<string> getTargetsPath,
        Func<string, int, Task> notifyScoreUpdate,
        IConnectivityChecker connectivity)
    {
        _getProfiles = getProfiles;
        _getActiveProfile = getActiveProfile;
        _switchProfile = switchProfile;
        _getTargetsPath = getTargetsPath;
        _notifyScoreUpdate = notifyScoreUpdate;
        _connectivity = connectivity;
        _probeService = new ProfileProbeService(_connectivity, _switchProfile);
    }

    public void Start()
    {
        if (_cts is not null) return;

        _cts = new CancellationTokenSource();
        Task.Run(() => LoopAsync(_cts.Token));
        Notify("Оркестратор запущен.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsScanning = false;
        NextCheckAt = null;
        _consecutiveFailures = 0;
        Notify("Оркестратор остановлен.");
    }

    public Task CheckNowAsync() => RunCheckAsync(CancellationToken.None);

    public async Task ScanAllProfilesAsync(CancellationToken ct = default)
    {
        await ScanAndRankAsync(ct).ConfigureAwait(false);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        if (_rankedProfiles.Count == 0)
        {
            await ScanAndRankAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
        }
        else
        {
            Notify("Рейтинг уже построен, сканирование пропущено.");
        }

        await StartBestProfileAsync(ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return;

        while (!ct.IsCancellationRequested)
        {
            NextCheckAt = DateTimeOffset.Now + CheckInterval;

            try
            {
                await Task.Delay(CheckInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunCheckAsync(ct).ConfigureAwait(false);
        }

        NextCheckAt = null;
    }

    private async Task ScanAndRankAsync(CancellationToken ct)
    {
        if (IsScanning)
        {
            Notify("Сканирование уже выполняется.");
            return;
        }

        IsScanning = true;

        try
        {
            var profiles = _getProfiles().ToList();

            if (profiles.Count == 0)
            {
                Notify("Нет профилей для сканирования.");
                return;
            }

            Notify($"Сканирование {profiles.Count} профилей с проверкой winws.exe и целей...");
            var targets = BuildTargets();
            var scores = new List<(ProfileItem profile, int score, ProfileProbeResult? result)>();

            for (var i = 0; i < profiles.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var profile = profiles[i];
                Notify($"[{i + 1}/{profiles.Count}] Тестирую «{profile.DisplayName}»...");
                await _notifyScoreUpdate(profile.FileName, -1).ConfigureAwait(false);

                var result = await _probeService.ProbeAsync(
                    profile,
                    targets,
                    new ProfileProbeOptions { StopAfterProbe = true },
                    ct).ConfigureAwait(false);

                scores.Add((profile, result.Score, result));

                Notify(
                    $"→ «{profile.DisplayName}»: {result.Score}% ({result.Summary}){(result.Score == 0 ? " ❌ исключён" : "")}",
                    result: result);

                foreach (var line in result.GetDetailLines().Take(4))
                    Notify($"   {line}", result: result);

                await _notifyScoreUpdate(profile.FileName, result.Score).ConfigureAwait(false);
            }

            _rankedProfiles = scores.OrderByDescending(x => x.score).ToList();

            var summary = string.Join(", ", _rankedProfiles.Take(3).Select(x => $"{x.profile.DisplayName}:{x.score}%"));
            Notify($"✅ Сканирование завершено.\nТоп: {summary}");
        }
        catch (OperationCanceledException)
        {
            Notify("Сканирование отменено.");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task StartBestProfileAsync(CancellationToken ct)
    {
        var best = _rankedProfiles.FirstOrDefault(x => x.score > 0);

        if (best.profile is null)
        {
            Notify("❌ Нет рабочих профилей. Запускаю первый по списку.");
            best = _rankedProfiles.FirstOrDefault();
        }

        if (best.profile is null)
        {
            Notify("Нет профилей.");
            return;
        }

        Notify($"Запускаю лучший профиль «{best.profile.DisplayName}» ({best.score}%).");
        await _switchProfile(best.profile).ConfigureAwait(false);
    }

    private async Task RunCheckAsync(CancellationToken ct)
    {
        var active = _getActiveProfile();
        var targets = BuildTargets();

        if (active is null)
        {
            Notify("Активный профиль не выбран. Переключаюсь на лучший доступный...");
            await SwitchToNextBestAsync(null, ct).ConfigureAwait(false);
            return;
        }

        Notify($"Проверка профиля «{active.DisplayName}»...");

        var result = await _probeService.ProbeCurrentAsync(active, targets, ct: ct).ConfigureAwait(false);
        var pct = result.Score;

        UpdateRank(active, pct, result);

        Notify($"Результат: {pct}% ({result.Summary})", result: result);

        if (result.IsWorking(FailThreshold))
        {
            _consecutiveFailures = 0;
            Notify($"✅ Профиль «{active.DisplayName}» работает ({pct}%).", result: result);
            return;
        }

        _consecutiveFailures++;

        if (_consecutiveFailures < RequiredFailuresBeforeSwitch)
        {
            Notify(
                $"⚠️ Профиль «{active.DisplayName}» ниже порога ({pct}%). Повторная проверка перед переключением: {_consecutiveFailures}/{RequiredFailuresBeforeSwitch}.",
                result: result);
            return;
        }

        _consecutiveFailures = 0;
        Notify($"⚠️ Профиль «{active.DisplayName}» не работает ({pct}%). Переключаю...", result: result);
        await SwitchToNextBestAsync(active, ct).ConfigureAwait(false);
    }

    private async Task SwitchToNextBestAsync(ProfileItem? current, CancellationToken ct)
    {
        var targets = BuildTargets();
        var candidates = _rankedProfiles
            .Where(x => x.score > 0 && !IsSameProfile(x.profile, current))
            .OrderByDescending(x => x.score)
            .ToList();

        if (candidates.Count == 0)
        {
            Notify("❌ Нет альтернативных рабочих профилей.");
            return;
        }

        foreach (var (profile, score, _) in candidates)
        {
            if (ct.IsCancellationRequested) return;

            Notify($"Пробую «{profile.DisplayName}» (рейтинг {score}%)...");

            var result = await _probeService.ProbeAsync(
                profile,
                targets,
                new ProfileProbeOptions { StopAfterProbe = false },
                ct).ConfigureAwait(false);

            UpdateRank(profile, result.Score, result);
            await _notifyScoreUpdate(profile.FileName, result.Score).ConfigureAwait(false);

            if (result.IsWorking(FailThreshold))
            {
                Notify($"✅ Переключились на «{profile.DisplayName}» ({result.Score}%).", switched: true, newProfile: profile.DisplayName, result: result);
                return;
            }

            Notify($"❌ «{profile.DisplayName}» тоже не работает ({result.Score}%). {result.Summary}", result: result);
        }

        await _switchProfile(null).ConfigureAwait(false);
        Notify("❌ Ни один профиль не прошёл проверку.");
    }

    private List<TargetEntry> BuildTargets()
    {
        var targets = TargetEntry.ParseFile(_getTargetsPath());

        foreach (var site in EnabledSites)
        {
            if (ConnectivityChecker.BuiltinSites.TryGetValue(site, out var entries))
                targets.AddRange(entries);
        }

        return targets
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => $"{x.Kind}|{x.Key}|{x.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private void UpdateRank(ProfileItem profile, int score, ProfileProbeResult result)
    {
        var updated = false;

        for (var i = 0; i < _rankedProfiles.Count; i++)
        {
            if (!IsSameProfile(_rankedProfiles[i].profile, profile)) continue;

            _rankedProfiles[i] = (profile, score, result);
            updated = true;
            break;
        }

        if (!updated)
            _rankedProfiles.Add((profile, score, result));

        _rankedProfiles = _rankedProfiles.OrderByDescending(x => x.score).ToList();
    }

    private static bool IsSameProfile(ProfileItem? a, ProfileItem? b)
    {
        if (a is null || b is null) return false;
        return string.Equals(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private void Notify(string msg, bool switched = false, string? newProfile = null, ProfileProbeResult? result = null)
    {
        StatusChanged?.Invoke(this, new OrchestratorEventArgs
        {
            Message = $"[{DateTime.Now:HH:mm:ss}] {msg}",
            IsSwitched = switched,
            NewProfile = newProfile,
            ProbeResult = result
        });
    }

    public void Dispose() => Stop();
}
