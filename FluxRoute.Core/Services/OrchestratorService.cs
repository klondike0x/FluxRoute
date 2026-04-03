using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public sealed class OrchestratorEventArgs : EventArgs
{
    public string Message { get; init; } = "";
    public bool IsSwitched { get; init; }
    public string? NewProfile { get; init; }
}

public sealed class OrchestratorService : IDisposable
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(20);
    public double FailThreshold { get; set; } = 0.5;
    public HashSet<string> EnabledSites { get; set; } = ["YouTube", "Discord", "Google", "Twitch", "Instagram", "Telegram"];

    public bool IsRunning => _cts is not null;
    public bool IsScanning { get; private set; }
    public DateTimeOffset? NextCheckAt { get; private set; }

    private List<(ProfileItem profile, int score)> _rankedProfiles = [];

    private readonly Func<IReadOnlyList<ProfileItem>> _getProfiles;
    private readonly Func<ProfileItem?> _getActiveProfile;
    private readonly Func<ProfileItem, Task> _switchProfile;
    private readonly Func<string> _getTargetsPath;
    private readonly Func<string, int, Task> _notifyScoreUpdate;

    private CancellationTokenSource? _cts;

    public event EventHandler<OrchestratorEventArgs>? StatusChanged;

    public OrchestratorService(
        Func<IReadOnlyList<ProfileItem>> getProfiles,
        Func<ProfileItem?> getActiveProfile,
        Func<ProfileItem, Task> switchProfile,
        Func<string> getTargetsPath,
        Func<string, int, Task> notifyScoreUpdate)
    {
        _getProfiles = getProfiles;
        _getActiveProfile = getActiveProfile;
        _switchProfile = switchProfile;
        _getTargetsPath = getTargetsPath;
        _notifyScoreUpdate = notifyScoreUpdate;
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
        Notify("Оркестратор остановлен.");
    }

    public Task CheckNowAsync() => RunCheckAsync(CancellationToken.None);

    public async Task ScanAllProfilesAsync(CancellationToken ct = default)
    {
        await ScanAndRankAsync(ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Сканируем только если рейтинг ещё не построен
        if (_rankedProfiles.Count == 0)
        {
            await ScanAndRankAsync(ct);
            if (ct.IsCancellationRequested) return;
        }
        else
        {
            Notify("📊 Рейтинг уже построен, сканирование пропущено.");
        }

        await StartBestProfileAsync(ct);
        if (ct.IsCancellationRequested) return;

        while (!ct.IsCancellationRequested)
        {
            NextCheckAt = DateTimeOffset.Now + CheckInterval;
            try { await Task.Delay(CheckInterval, ct); }
            catch (OperationCanceledException) { break; }

            await RunCheckAsync(ct);
        }

        NextCheckAt = null;
    }

    private async Task ScanAndRankAsync(CancellationToken ct)
    {
        IsScanning = true;
        var profiles = _getProfiles();
        if (profiles.Count == 0) { Notify("Нет профилей для сканирования."); IsScanning = false; return; }

        Notify($"📊 Сканирование {profiles.Count} профилей...");
        var targets = BuildTargets();
        var scores = new List<(ProfileItem profile, int score)>();

        for (int i = 0; i < profiles.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var profile = profiles[i];
            Notify($"[{i + 1}/{profiles.Count}] Тестирую «{profile.DisplayName}»...");
            await _notifyScoreUpdate(profile.FileName, -1);

            await _switchProfile(profile);
            await Task.Delay(5000, ct);

            var (rate, _) = await ConnectivityChecker.CheckAllAsync(targets, ct);
            int score = (int)(rate * 100);
            scores.Add((profile, score));

            Notify($"  → «{profile.DisplayName}»: {score}%{(score == 0 ? " ❌ исключён" : "")}");
            await _notifyScoreUpdate(profile.FileName, score);

            await _switchProfile(null!);
        }

        _rankedProfiles = scores.OrderByDescending(x => x.score).ToList();

        var summary = string.Join(", ", _rankedProfiles.Take(3).Select(x => $"{x.profile.DisplayName}:{x.score}%"));
        Notify($"✅ Сканирование завершено. Топ: {summary}");
        IsScanning = false;
    }

    private async Task StartBestProfileAsync(CancellationToken ct)
    {
        var best = _rankedProfiles.FirstOrDefault(x => x.score > 0);
        if (best.profile is null)
        {
            Notify("❌ Нет рабочих профилей. Запускаю первый по списку.");
            best = _rankedProfiles.FirstOrDefault();
        }

        if (best.profile is null) { Notify("Нет профилей."); return; }

        Notify($"🚀 Запускаю лучший профиль «{best.profile.DisplayName}» ({best.score}%)");
        await _switchProfile(best.profile);
    }

    private async Task RunCheckAsync(CancellationToken ct)
    {
        var active = _getActiveProfile();
        var targets = BuildTargets();

        Notify($"🔍 Проверка профиля «{active?.DisplayName ?? "—"}»...");
        var (rate, results) = await ConnectivityChecker.CheckAllAsync(targets, ct);
        var pct = (int)(rate * 100);

        var failed = results.Where(r => !r.Ok).Select(r => r.Key).ToList();
        var detail = failed.Count == 0 ? "все OK" : $"сбой: {string.Join(", ", failed.Take(3))}";
        Notify($"Результат: {pct}% ({detail})");

        if (active is not null)
        {
            for (int i = 0; i < _rankedProfiles.Count; i++)
                if (_rankedProfiles[i].profile == active)
                    { _rankedProfiles[i] = (active, pct); break; }
            _rankedProfiles = _rankedProfiles.OrderByDescending(x => x.score).ToList();
        }

        if (rate >= FailThreshold) { Notify($"✅ Профиль «{active?.DisplayName}» работает ({pct}%)."); return; }

        Notify($"⚠️ Профиль «{active?.DisplayName}» не работает ({pct}%). Переключаю...");
        await SwitchToNextBestAsync(active, ct);
    }

    private async Task SwitchToNextBestAsync(ProfileItem? current, CancellationToken ct)
    {
        var targets = BuildTargets();
        var candidates = _rankedProfiles.Where(x => x.score > 0 && x.profile != current).ToList();

        if (candidates.Count == 0) { Notify("❌ Нет альтернативных рабочих профилей."); return; }

        foreach (var (profile, score) in candidates)
        {
            if (ct.IsCancellationRequested) return;

            Notify($"🔄 Пробую «{profile.DisplayName}» (рейтинг {score}%)...");
            await _switchProfile(profile);
            await Task.Delay(5000, ct);

            var (rate, _) = await ConnectivityChecker.CheckAllAsync(targets, ct);
            var pct = (int)(rate * 100);

            if (rate >= FailThreshold)
            {
                Notify($"✅ Переключились на «{profile.DisplayName}» ({pct}%).", switched: true, newProfile: profile.DisplayName);
                return;
            }

            Notify($"❌ «{profile.DisplayName}» тоже не работает ({pct}%).");
        }

        Notify("❌ Ни один профиль не прошёл проверку.");
    }

    private List<TargetEntry> BuildTargets()
    {
        var targets = TargetEntry.ParseFile(_getTargetsPath());
        foreach (var site in EnabledSites)
            if (ConnectivityChecker.BuiltinSites.TryGetValue(site, out var entries))
                targets.AddRange(entries);
        return targets;
    }

    private void Notify(string msg, bool switched = false, string? newProfile = null)
        => StatusChanged?.Invoke(this, new OrchestratorEventArgs
        {
            Message = $"[{DateTime.Now:HH:mm:ss}] {msg}",
            IsSwitched = switched,
            NewProfile = newProfile
        });

    public void Dispose() => Stop();
}
