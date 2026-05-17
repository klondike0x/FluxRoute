using FluxRoute.AI.Models;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.AI.Services;

public sealed class AiOrchestratorService : IDisposable
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(20);
    public double FailThreshold { get; set; } = 0.5;
    public int RequiredFailuresBeforeSwitch { get; set; } = 2;
    public HashSet<string> EnabledSites { get; set; } =
        ["YouTube", "Discord", "Google", "Twitch", "Instagram", "Telegram"];

    public bool IsRunning => _cts is not null;
    public DateTimeOffset? NextCheckAt { get; private set; }

    private readonly Func<IEnumerable<ProfileItem>> _getProfiles;
    private readonly Func<ProfileItem?> _getActiveProfile;
    private readonly Func<ProfileItem?, Task> _switchProfile;
    private readonly Func<string> _getTargetsPath;
    private readonly Func<string, int, Task> _notifyScoreUpdate;
    private readonly Func<string> _engineDir;
    private readonly Func<AiSettings> _aiSettings;
    private readonly Func<Task> _refreshProfiles;
    private readonly Func<bool> _isWinwsRunning;
    private readonly Func<Task> _ensureProtectionRunning;
    private readonly IConnectivityChecker _connectivity;
    private readonly ProfileProbeService _probeService;
    private readonly NetworkFingerprintProvider _fingerprints;
    private readonly NetworkChangeWatcher _networkWatcher;
    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly BanditSelector _bandit;
    private readonly StrategyEvolver _evolver;
    private readonly BatMaterializer _materializer;

    private CancellationTokenSource? _cts;
    private int _consecutiveFailures;
    private int _probeCountSinceEvolve;
    private DateTimeOffset _lastEvolutionUtc = DateTimeOffset.MinValue;
    private volatile bool _networkDirty;
    private StrategyGenome? _currentGenome;

    public event EventHandler<OrchestratorEventArgs>? StatusChanged;

    public AiOrchestratorService(
        Func<IEnumerable<ProfileItem>> getProfiles,
        Func<ProfileItem?> getActiveProfile,
        Func<ProfileItem?, Task> switchProfile,
        Func<string> getTargetsPath,
        Func<string, int, Task> notifyScoreUpdate,
        Func<string> engineDir,
        Func<AiSettings> aiSettings,
        Func<Task> refreshProfiles,
        Func<bool> isWinwsRunning,
        Func<Task> ensureProtectionRunning,
        IConnectivityChecker connectivity,
        NetworkFingerprintProvider fingerprints,
        NetworkChangeWatcher networkWatcher,
        AiStrategyRegistry registry,
        AiHistoryStore history,
        BanditSelector bandit,
        StrategyEvolver evolver,
        BatMaterializer materializer)
    {
        _getProfiles = getProfiles;
        _getActiveProfile = getActiveProfile;
        _switchProfile = switchProfile;
        _getTargetsPath = getTargetsPath;
        _notifyScoreUpdate = notifyScoreUpdate;
        _engineDir = engineDir;
        _aiSettings = aiSettings;
        _refreshProfiles = refreshProfiles;
        _isWinwsRunning = isWinwsRunning;
        _ensureProtectionRunning = ensureProtectionRunning;
        _connectivity = connectivity;
        _fingerprints = fingerprints;
        _networkWatcher = networkWatcher;
        _registry = registry;
        _history = history;
        _bandit = bandit;
        _evolver = evolver;
        _materializer = materializer;
        _probeService = new ProfileProbeService(_connectivity, _switchProfile);
    }

    public void SyncRegistryFromEngine() => SyncBuiltins();

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _networkWatcher.NetworkChanged += OnNetworkChanged;
        Task.Run(() => LoopAsync(_cts.Token));
        Notify("ИИ-оркестратор запущен.");
    }

    public void Stop()
    {
        _networkWatcher.NetworkChanged -= OnNetworkChanged;
        _cts?.Cancel();
        _cts = null;
        NextCheckAt = null;
        _consecutiveFailures = 0;
        _currentGenome = null;
        Notify("ИИ-оркестратор остановлен.");
    }

    public Task CheckNowAsync() => RunCycleAsync(CancellationToken.None);

    public async Task ProbeAllEnabledStrategiesAsync(CancellationToken ct = default)
    {
        var previousGenome = _currentGenome;
        var previousProfile = _getActiveProfile();
        var wasRunning = _isWinwsRunning();
        try
        {
            await _refreshProfiles().ConfigureAwait(false);
            var fp = _fingerprints.Capture();
            _registry.MarkNetworkSeen(fp.Hash);
            _registry.Save();

            var list = _registry.GetActiveGenomes().ToList();
            if (list.Count == 0)
            {
                Notify("ИИ: нет отмеченных стратегий для проверки.");
                return;
            }

            Notify($"ИИ: ручная проверка {list.Count} стратегий...");
            foreach (var g in list)
                await TryProbeAndPersistGenomeAsync(g, fp, ct, isFreshlyEvolved: false).ConfigureAwait(false);
        }
        finally
        {
            if (previousProfile is not null)
                await _switchProfile(previousProfile).ConfigureAwait(false);
            else if (wasRunning)
                await _ensureProtectionRunning().ConfigureAwait(false);

            _currentGenome = previousGenome;
        }
    }

    private void OnNetworkChanged(object? sender, (NetworkFingerprint OldFp, NetworkFingerprint NewFp) e) =>
        _networkDirty = true;

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            SyncBuiltins();
            await _refreshProfiles().ConfigureAwait(false);

            await PickAndApplyInitialAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
                return;

            while (!ct.IsCancellationRequested)
            {
                var interval = _networkDirty ? TimeSpan.FromSeconds(2) : CheckInterval;
                _networkDirty = false;
                NextCheckAt = DateTimeOffset.Now + interval;

                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RunCycleAsync(ct).ConfigureAwait(false);
            }

            NextCheckAt = null;
        }
        catch (Exception ex)
        {
            Notify($"ИИ: цикл прервался: {ex.Message}");
        }
    }

    private async Task PickAndApplyInitialAsync(CancellationToken ct)
    {
        var fp = _fingerprints.Capture();
        _registry.MarkNetworkSeen(fp.Hash);
        _registry.Save();

        var pool = GenePool();
        if (pool.Count == 0)
        {
            Notify(_registry.GetGenomes().Count > 0
                ? "ИИ: нет включённых стратегий. Включите хотя бы одну на вкладке «Оркестратор»."
                : "ИИ: нет профилей/genomes в engine/. Обновите Flowseal.");
            return;
        }

        StrategyGenome? pick = null;
        if (_registry.TotalPullsOnNetwork(fp.Hash) >= 1)
            pick = _bandit.BestKnownForNetwork(pool, fp.Hash);

        pick ??= _bandit.Pick(pool, fp.Hash, _aiSettings().ExplorationRatePermil);

        if (pick is null)
        {
            Notify("ИИ: не удалось выбрать стратегию.");
            return;
        }

        await ApplyGenomeAsync(pick, fp, ct).ConfigureAwait(false);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var ai = _aiSettings();
        _history.RotateOldEntries(ai.KeepHistoryDays);

        var fp = _fingerprints.Capture();
        _registry.MarkNetworkSeen(fp.Hash);

        if (_networkDirty)
        {
            _networkDirty = false;
            await RepickAfterNetworkChangeAsync(fp, ct).ConfigureAwait(false);
            return;
        }

        if (_currentGenome is not null)
        {
            var fresh = _registry.GetById(_currentGenome.Id);
            if (fresh is null || !fresh.OrchestratorEnabled)
            {
                Notify("ИИ: текущая стратегия отключена, переподбор...");
                _currentGenome = null;
                await RepickAfterNetworkChangeAsync(fp, ct).ConfigureAwait(false);
                return;
            }

            _currentGenome = fresh;
        }

        var active = _getActiveProfile();
        if (active is null || _currentGenome is null)
        {
            await RepickAfterNetworkChangeAsync(fp, ct).ConfigureAwait(false);
            return;
        }

        Notify($"ИИ проверка «{active.DisplayName}»...");

        var targets = BuildTargets();
        var result = await _probeService.ProbeCurrentAsync(active, targets, ct: ct).ConfigureAwait(false);

        var failedKeys = result.FailedChecks.Select(x => x.Key).ToList();
        var avgLat = result.Checks.Where(x => x.ElapsedMs.HasValue).Select(x => x.ElapsedMs!.Value).DefaultIfEmpty(0)
            .Average();

        var failureSig = !result.ProcessStable
            ? "winws_failed"
            : result.Score < (int)Math.Round(FailThreshold * 100)
                ? "network_failed"
                : null;

        var outcome = new ProbeOutcome
        {
            GenomeId = _currentGenome.Id,
            NetworkHash = fp.Hash,
            Timestamp = DateTimeOffset.UtcNow,
            Score = result.Score,
            SuccessRate = result.SuccessRate,
            AvgLatencyMs = avgLat,
            ProcessStable = result.ProcessStable,
            FailedTargetKeys = failedKeys,
            FailureSignature = failureSig,
        };

        _history.Append(outcome);

        if (result.IsWorking(FailThreshold))
        {
            _registry.RecordBanditSuccess(_currentGenome.Id, fp.Hash);
            _bandit.RegisterSuccess(_currentGenome.Id);
            _consecutiveFailures = 0;
            Notify($"✅ ИИ: «{active.DisplayName}» ок ({result.Score}%).", result: result);
        }
        else
        {
            _registry.RecordBanditFailure(_currentGenome.Id, fp.Hash);
            _bandit.RegisterFailure(_currentGenome, failureSig);
            Notify($"⚠️ ИИ: «{active.DisplayName}» {result.Score}% ({result.Summary})", result: result);
            _consecutiveFailures++;
        }

        await _notifyScoreUpdate(active.FileName, result.Score).ConfigureAwait(false);
        _registry.Save();

        _probeCountSinceEvolve++;
        await MaybeEvolveAsync(fp, ct).ConfigureAwait(false);

        if (result.IsWorking(FailThreshold))
            return;

        if (_consecutiveFailures < RequiredFailuresBeforeSwitch)
        {
            Notify(
                $"ИИ: повтор перед сменой {_consecutiveFailures}/{RequiredFailuresBeforeSwitch}.");
            return;
        }

        _consecutiveFailures = 0;
        await SwitchToAlternativeAsync(fp, ct).ConfigureAwait(false);
    }

    private async Task RepickAfterNetworkChangeAsync(NetworkFingerprint fp, CancellationToken ct)
    {
        Notify($"ИИ: смена сети ({fp.Label}), переподбор...");
        var pool = GenePool();
        var pick = _bandit.BestKnownForNetwork(pool, fp.Hash)
                   ?? _bandit.Pick(pool, fp.Hash, _aiSettings().ExplorationRatePermil);

        if (pick is null)
        {
            Notify(GenePool().Count == 0
                ? "ИИ: нет включённых стратегий для переподбора."
                : "ИИ: нет доступной стратегии после смены сети.");
            return;
        }

        await ApplyGenomeAsync(pick, fp, ct, switched: true).ConfigureAwait(false);
    }

    private async Task SwitchToAlternativeAsync(NetworkFingerprint fp, CancellationToken ct)
    {
        var currentId = _currentGenome?.Id;
        var pool = GenePool().Where(g => g.Id != currentId).ToList();
        var pick = _bandit.Pick(pool, fp.Hash, _aiSettings().ExplorationRatePermil);

        if (pick is null)
        {
            Notify("❌ ИИ: нет альтернативных стратегий.");
            return;
        }

        await ApplyGenomeAsync(pick, fp, ct, switched: true).ConfigureAwait(false);
    }

    private async Task ApplyGenomeAsync(StrategyGenome g, NetworkFingerprint fp, CancellationToken ct,
        bool switched = false)
    {
        await _refreshProfiles().ConfigureAwait(false);
        var profile = ResolveProfile(g);
        if (profile is null)
        {
            Notify($"ИИ: не удалось материализовать профиль для «{g.DisplayName}».");
            return;
        }

        _currentGenome = g;
        Notify(
            switched
                ? $"ИИ: переключение на «{g.DisplayName}» ({fp.Label})."
                : $"ИИ: запуск «{g.DisplayName}» ({fp.Label}).",
            switched: switched,
            newProfile: g.DisplayName);

        await _switchProfile(profile).ConfigureAwait(false);
    }

    private async Task MaybeEvolveAsync(NetworkFingerprint fp, CancellationToken ct)
    {
        var ai = _aiSettings();
        var now = DateTimeOffset.UtcNow;
        if (_probeCountSinceEvolve < ai.MinProbesBeforeEvolve)
            return;

        var interval = TimeSpan.FromMinutes(Math.Max(5, ai.EvolutionIntervalMinutes));
        if (now - _lastEvolutionUtc < interval)
            return;

        _lastEvolutionUtc = now;
        _probeCountSinceEvolve = 0;

        Notify("ИИ: эволюция стратегий...");
        var child = await Task.Run(() => _evolver.Evolve(fp), ct).ConfigureAwait(false);
        SyncBuiltins();
        await _refreshProfiles().ConfigureAwait(false);
        if (child is not null)
        {
            Notify("ИИ: эволюция завершена, новые профили в engine/ai-evolved/.");
            await VerifyEvolvedGenomeAsync(child, fp, ct).ConfigureAwait(false);
        }
        else
            Notify("ИИ: эволюция не дала новой стратегии (мало вариантов в пуле или нет родителей).");
    }

    public async Task EvolveNowAsync(CancellationToken ct = default)
    {
        SyncBuiltins();
        var fp = _fingerprints.Capture();
        var child = await Task.Run(() => _evolver.Evolve(fp), ct).ConfigureAwait(false);
        await _refreshProfiles().ConfigureAwait(false);
        if (child is not null)
            await VerifyEvolvedGenomeAsync(child, fp, ct).ConfigureAwait(false);
        else
            Notify("ИИ: эволюция не создала новую стратегию (мало активных родителей или дубликат).");
    }

    private ProfileItem? ResolveProfile(StrategyGenome g)
    {
        var engineDir = _engineDir();

        string? path = null;
        if (!string.IsNullOrEmpty(g.SourceBatPath) && File.Exists(g.SourceBatPath))
            path = g.SourceBatPath;
        else if (!string.IsNullOrEmpty(g.BatFileName))
        {
            path = Path.Combine(engineDir, "ai-evolved", g.BatFileName);
            if (!File.Exists(path))
            {
                path = _materializer.WriteBat(g, engineDir);
                g.SourceBatPath = path;
                g.BatFileName = Path.GetFileName(path);
                _registry.Upsert(g);
                _registry.Save();
            }
        }
        else if (g.Origin == StrategyOrigin.Evolved)
        {
            path = _materializer.WriteBat(g, engineDir);
            g.SourceBatPath = path;
            g.BatFileName = Path.GetFileName(path);
            _registry.Upsert(g);
            _registry.Save();
        }

        if (path is null || !File.Exists(path))
            return null;

        return new ProfileItem
        {
            FileName = Path.GetFileName(path),
            DisplayName = g.DisplayName,
            FullPath = path,
        };
    }

    private List<StrategyGenome> GenePool() =>
        _registry.GetActiveGenomes().ToList();

    private async Task VerifyEvolvedGenomeAsync(StrategyGenome child, NetworkFingerprint fp, CancellationToken ct)
    {
        var previousGenome = _currentGenome;
        var previousProfile = _getActiveProfile();
        var wasRunning = _isWinwsRunning();
        try
        {
            await _refreshProfiles().ConfigureAwait(false);
            await TryProbeAndPersistGenomeAsync(child, fp, ct, isFreshlyEvolved: true).ConfigureAwait(false);
        }
        finally
        {
            if (previousProfile is not null)
                await _switchProfile(previousProfile).ConfigureAwait(false);
            else if (wasRunning)
                await _ensureProtectionRunning().ConfigureAwait(false);

            _currentGenome = previousGenome;
        }
    }

    private async Task TryProbeAndPersistGenomeAsync(StrategyGenome g, NetworkFingerprint fp, CancellationToken ct,
        bool isFreshlyEvolved)
    {
        var testProfile = ResolveProfile(g);
        if (testProfile is null)
        {
            Notify($"ИИ: не удалось проверить «{g.DisplayName}».");
            return;
        }

        Notify(isFreshlyEvolved
            ? $"ИИ: проверка новой стратегии «{g.DisplayName}»..."
            : $"ИИ: ручная проверка «{g.DisplayName}»...");
        var targets = BuildTargets();
        var probeOptions = new ProfileProbeOptions
        {
            StartupWait = TimeSpan.FromSeconds(5),
            StableWait = TimeSpan.FromSeconds(1.5),
            ProcessWaitTimeout = TimeSpan.FromSeconds(8),
            StopAfterProbe = false,
        };
        var result = await _probeService.ProbeAsync(testProfile, targets, probeOptions, ct).ConfigureAwait(false);

        var failedKeys = result.FailedChecks.Select(x => x.Key).ToList();
        var avgLat = result.Checks.Where(x => x.ElapsedMs.HasValue).Select(x => x.ElapsedMs!.Value).DefaultIfEmpty(0)
            .Average();

        var failureSig = !result.ProcessStable
            ? "winws_failed"
            : result.Score < (int)Math.Round(FailThreshold * 100)
                ? "network_failed"
                : null;

        var outcome = new ProbeOutcome
        {
            GenomeId = g.Id,
            NetworkHash = fp.Hash,
            Timestamp = DateTimeOffset.UtcNow,
            Score = result.Score,
            SuccessRate = result.SuccessRate,
            AvgLatencyMs = avgLat,
            ProcessStable = result.ProcessStable,
            FailedTargetKeys = failedKeys,
            FailureSignature = failureSig,
        };

        _history.Append(outcome);

        if (result.IsWorking(FailThreshold))
        {
            _registry.RecordBanditSuccess(g.Id, fp.Hash);
            _bandit.RegisterSuccess(g.Id);
            Notify(isFreshlyEvolved
                    ? $"✅ ИИ: новая стратегия «{g.DisplayName}» ок ({result.Score}%)."
                    : $"✅ ИИ: «{g.DisplayName}» ок ({result.Score}%).",
                result: result);
        }
        else
        {
            _registry.RecordBanditFailure(g.Id, fp.Hash);
            _bandit.RegisterFailure(g, failureSig);
            Notify(isFreshlyEvolved
                    ? $"⚠️ ИИ: новая стратегия «{g.DisplayName}» {result.Score}% ({result.Summary})"
                    : $"⚠️ ИИ: «{g.DisplayName}» {result.Score}% ({result.Summary})",
                result: result);
        }

        await _notifyScoreUpdate(testProfile.FileName, result.Score).ConfigureAwait(false);

        g.LastVerificationScore = result.Score;
        g.LastVerifiedAt = DateTimeOffset.UtcNow;
        _registry.Upsert(g);
        _registry.Save();
    }

    private void SyncBuiltins()
    {
        var engineDir = _engineDir();
        if (!Directory.Exists(engineDir))
            return;

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat", "service,.bat" };

        foreach (var bat in Directory.EnumerateFiles(engineDir, "*.bat", SearchOption.TopDirectoryOnly))
        {
            var fn = Path.GetFileName(bat);
            if (excluded.Contains(fn))
                continue;

            if (!ProfileBatLauncher.TryCreateLaunchPlan(bat, engineDir, out var plan, out _) || plan is null)
                continue;

            var name = Path.GetFileNameWithoutExtension(bat);
            var genome = BatGenomeParser.FromLaunchPlan(plan, name, StrategyOrigin.Builtin);
            genome.Id = StableGuid.FromString("builtin:" + Path.GetFullPath(bat));
            genome.SourceBatPath = bat;
            genome.BatFileName = fn;
            genome.DisplayName = name;
            var existing = _registry.GetById(genome.Id);
            if (existing is not null)
            {
                genome.OrchestratorEnabled = existing.OrchestratorEnabled;
                genome.LastVerificationScore = existing.LastVerificationScore;
                genome.LastVerifiedAt = existing.LastVerifiedAt;
            }

            _registry.Upsert(genome);
        }

        _registry.Save();
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

    private void Notify(string msg, bool switched = false, string? newProfile = null,
        ProfileProbeResult? result = null)
    {
        StatusChanged?.Invoke(this, new OrchestratorEventArgs
        {
            Message = $"[{DateTime.Now:HH:mm:ss}] {msg}",
            IsSwitched = switched,
            NewProfile = newProfile,
            ProbeResult = result,
        });
    }

    public void Dispose() => Stop();
}
