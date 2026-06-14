using FluxRoute.AI.Models;
using FluxRoute.AI.Stats;
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
    public List<TargetEntry> UserSiteTargets { get; set; } = [];

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
    private readonly Func<bool> _isAnyEngineRunning;
    private readonly Func<Task> _ensureProtectionRunning;
    private readonly IConnectivityChecker _connectivity;
    private readonly DpiEngineManager _engineManager;
    private readonly ProfileProbeService _probeService;
    private readonly NetworkFingerprintProvider _fingerprints;
    private readonly NetworkChangeWatcher _networkWatcher;
    private readonly AiStrategyRegistry _registry;
    private readonly AiHistoryStore _history;
    private readonly BanditSelector _bandit;
    private readonly StrategyEvolver _evolver;

    private CancellationTokenSource? _cts;
    private int _consecutiveFailures;
    private int _probeCountSinceEvolve;
    private int _probeCountSinceMtuTune;
    private DateTimeOffset _lastEvolutionUtc = DateTimeOffset.MinValue;
    private volatile bool _networkDirty;
    private StrategyGenome? _currentGenome;
    private readonly Dictionary<string, (int score, DateTimeOffset at)> _networkProbeCache = new();
    private bool _fastStartDone;

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
        Func<bool> isAnyEngineRunning,
        Func<Task> ensureProtectionRunning,
        IConnectivityChecker connectivity,
        DpiEngineManager engineManager,
        NetworkFingerprintProvider fingerprints,
        NetworkChangeWatcher networkWatcher,
        AiStrategyRegistry registry,
        AiHistoryStore history,
        BanditSelector bandit,
        StrategyEvolver evolver)
    {
        _getProfiles = getProfiles;
        _getActiveProfile = getActiveProfile;
        _switchProfile = switchProfile;
        _getTargetsPath = getTargetsPath;
        _notifyScoreUpdate = notifyScoreUpdate;
        _engineDir = engineDir;
        _aiSettings = aiSettings;
        _refreshProfiles = refreshProfiles;
        _isAnyEngineRunning = isAnyEngineRunning;
        _ensureProtectionRunning = ensureProtectionRunning;
        _connectivity = connectivity;
        _engineManager = engineManager;
        _fingerprints = fingerprints;
        _networkWatcher = networkWatcher;
        _registry = registry;
        _history = history;
        _bandit = bandit;
        _evolver = evolver;
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
        var wasRunning = _isAnyEngineRunning();
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
            {
                var deleted = await TryProbeAndPersistGenomeAsync(g, fp, ct, isFreshlyEvolved: false).ConfigureAwait(false);
                if (deleted && _currentGenome?.Id == g.Id)
                    _currentGenome = null;
            }
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

            if (!_fastStartDone && _aiSettings().FastStartEnabled)
            {
                _fastStartDone = true;
                await FastStartProbeAsync(ct).ConfigureAwait(false);
            }

            while (!ct.IsCancellationRequested)
            {
                var baseInterval = _networkDirty ? TimeSpan.FromSeconds(2) : CheckInterval;
                if (_consecutiveFailures > 0 && !_networkDirty)
                    baseInterval = TimeSpan.FromSeconds(Math.Max(15, baseInterval.TotalSeconds / Math.Pow(2, _consecutiveFailures)));
                else if (!_networkDirty && _consecutiveFailures == 0)
                    baseInterval = TimeSpan.FromSeconds(Math.Min(1800, baseInterval.TotalSeconds * 1.5));
                var interval = baseInterval;
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

        // Network probe cache: skip if we recently probed this genome on this network with good result
        if (_currentGenome is not null && _networkProbeCache.TryGetValue($"{fp.Hash}|{_currentGenome.Id}", out var cached) &&
            (DateTimeOffset.UtcNow - cached.at).TotalMinutes < 10 && cached.score >= 80)
        {
            Notify($"ИИ: пропуск проверки (кеш: {cached.score}% за {Math.Round((DateTimeOffset.UtcNow - cached.at).TotalMinutes)}м).");
            return;
        }

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

        // If Warp is enabled in current run mode, check it too
        var runMode = _engineManager.RunMode;
        if (runMode == DpiRunMode.Warp || runMode == DpiRunMode.WarpZapret || runMode == DpiRunMode.WarpByeDpi)
        {
            var warpCheck = await _connectivity.CheckWarpAsync(ct).ConfigureAwait(false);
            result.Checks.Add(warpCheck);
            // Re-calculate score with warp check
            result.Score = ProfileScoringService.Calculate(result.ProcessStarted, result.ProcessStable, result.Checks, true);
        }

        var failedKeys = result.FailedChecks.Select(x => x.Key).ToList();
        var avgLat = result.Checks.Where(x => x.ElapsedMs.HasValue).Select(x => x.ElapsedMs!.Value).DefaultIfEmpty(0)
            .Average();

        var engineName = _currentGenome.EngineType switch
        {
            DpiEngineType.ByeDpi => "byedpi",
            DpiEngineType.Warp => "warp",
            _ => "zapret"
        };
        var failureSig = !result.ProcessStable
            ? $"{engineName}_failed"
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

        // Update network probe cache
        if (_currentGenome is not null)
        {
            var cacheKey = $"{fp.Hash}|{_currentGenome.Id}";
            _networkProbeCache[cacheKey] = (result.Score, DateTimeOffset.UtcNow);
            // Evict old entries
            if (_networkProbeCache.Count > _aiSettings().NetworkCacheSize)
            {
                var oldest = _networkProbeCache.OrderBy(kv => kv.Value.at).First();
                _networkProbeCache.Remove(oldest.Key);
            }
        }

        var current = _currentGenome;
        if (current is null) return;

        if (result.IsWorking(FailThreshold))
        {
            _registry.RecordBanditSuccess(current.Id, fp.Hash);
            _bandit.RegisterSuccess(current.Id);
            _consecutiveFailures = 0;
            Notify($"✅ ИИ: «{active.DisplayName}» ок ({result.Score}%).", result: result);

            if (current.EngineType == DpiEngineType.Warp && !string.IsNullOrWhiteSpace(current.WarpConfig))
            {
                SaveWarpConfig(current);
            }
        }
        else
        {
            _registry.RecordBanditFailure(current.Id, fp.Hash);
            _bandit.RegisterFailure(current, failureSig);
            Notify($"⚠️ ИИ: «{active.DisplayName}» {result.Score}% ({result.Summary})", result: result);
            _consecutiveFailures++;
        }

        await _notifyScoreUpdate(active.FileName, result.Score).ConfigureAwait(false);
        _registry.Save();

        _probeCountSinceEvolve++;
        await MaybeEvolveAsync(fp, ct).ConfigureAwait(false);

        _probeCountSinceMtuTune++;
        if (_currentGenome?.EngineType == DpiEngineType.Warp)
            await MaybeTuneMtuAsync(fp, ct).ConfigureAwait(false);

        if (result.IsWorking(FailThreshold))
            return;

        if (_consecutiveFailures < RequiredFailuresBeforeSwitch)
        {
            Notify($"ИИ: повтор перед сменой {_consecutiveFailures}/{RequiredFailuresBeforeSwitch}.");
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
            Notify(pool.Count == 0
                ? $"ИИ: нет включённых стратегий для режима {(_aiSettings().EngineMode switch { 1 => "ByeDPI", 2 => "Hybrid", _ => "Zapret" })}."
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

        var engineDir = _engineDir();
        var profile = g.ToEngineProfile();

        var runMode = _aiSettings().EngineMode == 2 ? DpiRunMode.Hybrid : DpiRunMode.Standalone;
        _engineManager.SetRunMode(runMode);

        var started = await _engineManager.ApplyProfileAsync(profile, ct).ConfigureAwait(false);
        if (!started)
        {
            Notify($"ИИ: не удалось запустить движок для «{g.DisplayName}».");
            return;
        }

        _currentGenome = g;
        Notify(
            switched
                ? $"ИИ: переключение на «{g.DisplayName}» ({g.EngineType}) ({fp.Label})."
                : $"ИИ: запуск «{g.DisplayName}» ({g.EngineType}) ({fp.Label}).",
            switched: switched,
            newProfile: g.DisplayName);

        if (_switchProfile is not null)
        {
            var pi = new ProfileItem
            {
                FileName = $"{g.DisplayName}.bat",
                DisplayName = g.DisplayName,
                FullPath = Path.Combine(engineDir, "ai-evolved", $"{g.DisplayName}.bat"),
            };
            await _switchProfile(pi).ConfigureAwait(false);
        }
    }

    private async Task FastStartProbeAsync(CancellationToken ct)
    {
        var pool = GenePool();
        if (pool.Count == 0) return;

        var fp = _fingerprints.Capture();
        var outcomes = _history.LoadForNetwork(fp.Hash);
        var byGenome = outcomes.GroupBy(o => o.GenomeId).ToDictionary(g => g.Key, g => g.ToList());

        var top = pool
            .Select(g =>
            {
                byGenome.TryGetValue(g.Id, out var list);
                list ??= [];
                var succ = list.Count(o => o.Score >= 50);
                var trials = list.Count;
                var wilson = WilsonScore.LowerBound(succ, trials);
                return (g, wilson);
            })
            .OrderByDescending(x => x.wilson)
            .Take(3)
            .Select(x => x.g)
            .ToList();

        Notify($"⚡ Быстрый старт: проверка {top.Count} топовых стратегий...");

        StrategyGenome? bestGenome = null;
        int bestScore = -1;

        var previousGenome = _currentGenome;
        var previousProfile = _getActiveProfile();
        var wasRunning = _isAnyEngineRunning();

        try
        {
            foreach (var g in top)
            {
                if (ct.IsCancellationRequested) break;
                await TryProbeAndPersistGenomeAsync(g, fp, ct, isFreshlyEvolved: false).ConfigureAwait(false);

                var freshG = _registry.GetById(g.Id);
                if (freshG?.LastVerificationScore is { } score && score > bestScore)
                {
                    bestScore = score;
                    bestGenome = freshG;
                }

                if (bestScore >= 95) break;
            }
        }
        finally
        {
            if (bestGenome != null && bestScore >= (int)Math.Round(FailThreshold * 100))
            {
                await ApplyGenomeAsync(bestGenome, fp, ct).ConfigureAwait(false);
            }
            else
            {
                if (previousProfile is not null)
                    await _switchProfile(previousProfile).ConfigureAwait(false);
                else if (wasRunning)
                    await _ensureProtectionRunning().ConfigureAwait(false);
                _currentGenome = previousGenome;
            }
        }
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

    private async Task MaybeTuneMtuAsync(NetworkFingerprint fp, CancellationToken ct)
    {
        if (_probeCountSinceMtuTune < 5) return;
        _probeCountSinceMtuTune = 0;

        var current = _currentGenome;
        if (current == null || current.EngineType != DpiEngineType.Warp) return;

        var lastOutcomes = _history.LoadFor(current.Id, fp.Hash).TakeLast(5).ToList();
        if (lastOutcomes.Count < 5) return;

        var avgScore = lastOutcomes.Average(o => o.Score);
        if (avgScore < 80)
        {
            var oldMtu = current.MTU ?? 1280;
            var newMtu = oldMtu - 20;
            if (newMtu < 1200) newMtu = 1420; // reset to high

            Notify($"ИИ: Авто-подбор MTU для Warp: {oldMtu} -> {newMtu} (avg score: {avgScore:F1}%)");
            current.MTU = newMtu;
            _registry.Upsert(current);
            _registry.Save();
            await ApplyGenomeAsync(current, fp, ct, switched: false).ConfigureAwait(false);
        }
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

    private List<StrategyGenome> GenePool()
    {
        var ai = _aiSettings();
        var all = _registry.GetActiveGenomes();
        return ai.EngineMode switch
        {
            (int)DpiEngineMode.ByeDpi => all.Where(g => g.EngineType == DpiEngineType.ByeDpi).ToList(),
            (int)DpiEngineMode.Warp => all.Where(g => g.EngineType == DpiEngineType.Warp).ToList(),
            (int)DpiEngineMode.Hybrid => all.Where(g => g.EngineType is DpiEngineType.Zapret or DpiEngineType.ByeDpi).ToList(),
            (int)DpiEngineMode.WarpZapret => all.Where(g => g.EngineType is DpiEngineType.Warp or DpiEngineType.Zapret).ToList(),
            (int)DpiEngineMode.WarpByeDpi => all.Where(g => g.EngineType is DpiEngineType.Warp or DpiEngineType.ByeDpi).ToList(),
            _ => all.Where(g => g.EngineType == DpiEngineType.Zapret).ToList(),
        };
    }

    private async Task VerifyEvolvedGenomeAsync(StrategyGenome child, NetworkFingerprint fp, CancellationToken ct)
    {
        var previousGenome = _currentGenome;
        var previousProfile = _getActiveProfile();
        var wasRunning = _isAnyEngineRunning();
        var deleted = false;
        try
        {
            await _refreshProfiles().ConfigureAwait(false);
            deleted = await TryProbeAndPersistGenomeAsync(child, fp, ct, isFreshlyEvolved: true).ConfigureAwait(false);
        }
        finally
        {
            if (previousProfile is not null)
                await _switchProfile(previousProfile).ConfigureAwait(false);
            else if (wasRunning)
                await _ensureProtectionRunning().ConfigureAwait(false);
            // Если удалили проверяемую стратегию и она была активной — сбрасываем текущий геном
            _currentGenome = deleted && _currentGenome?.Id == child.Id ? null : previousGenome;
        }
    }

    private async Task<bool> TryProbeAndPersistGenomeAsync(StrategyGenome g, NetworkFingerprint fp, CancellationToken ct,
        bool isFreshlyEvolved)
    {
        var engineDir = _engineDir();
        var profile = g.ToEngineProfile();

        var runMode = _aiSettings().EngineMode == 2 ? DpiRunMode.Hybrid : DpiRunMode.Standalone;
        _engineManager.SetRunMode(runMode);

        var started = await _engineManager.ApplyProfileAsync(profile, ct).ConfigureAwait(false);
        if (!started)
        {
            Notify($"ИИ: не удалось запустить движок для проверки «{g.DisplayName}».");
            return false;
        }

        var testProfile = new ProfileItem
        {
            FileName = $"{g.DisplayName}.bat",
            DisplayName = g.DisplayName,
            FullPath = Path.Combine(engineDir, "ai-evolved", $"{g.DisplayName}.bat"),
        };

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

        var socksPort = g.EngineType switch
        {
            DpiEngineType.ByeDpi => g.ToEngineProfile().SocksPort,
            DpiEngineType.Warp => 8086,
            _ => (int?)null
        };

        if (socksPort is not null)
        {
            probeOptions = new ProfileProbeOptions
            {
                StartupWait = probeOptions.StartupWait,
                StableWait = probeOptions.StableWait,
                ProcessWaitTimeout = probeOptions.ProcessWaitTimeout,
                StopAfterProbe = false,
                RequireWinwsProcess = false,
                UseCurlForHttp = probeOptions.UseCurlForHttp,
                MaxParallelChecks = probeOptions.MaxParallelChecks,
                Socks5Endpoint = $"127.0.0.1:{socksPort}",
                ProcessName = g.EngineType == DpiEngineType.Warp ? "warp-plus" : "ciadpi",
            };
        }
        var result = await _probeService.ProbeCurrentAsync(testProfile, targets, probeOptions, ct).ConfigureAwait(false);
        var failedKeys = result.FailedChecks.Select(x => x.Key).ToList();
        var avgLat = result.Checks.Where(x => x.ElapsedMs.HasValue).Select(x => x.ElapsedMs!.Value).DefaultIfEmpty(0)
            .Average();

        var engineName = g.EngineType switch
        {
            DpiEngineType.ByeDpi => "byedpi",
            DpiEngineType.Warp => "warp",
            _ => "zapret"
        };
        var failureSig = !result.ProcessStable
            ? $"{engineName}_failed"
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

            if (g.EngineType == DpiEngineType.Warp && !string.IsNullOrWhiteSpace(g.WarpConfig))
            {
                SaveWarpConfig(g);
            }
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

        g.LastVerificationScore = result.Score;
        g.LastVerifiedAt = DateTimeOffset.UtcNow;
        _registry.Upsert(g);
        _registry.Save();

        // ═══ АВТОУДАЛЕНИЕ НЕУДАЧНЫХ ЭВОЛЮЦИОНИРОВАННЫХ СТРАТЕГИЙ ═══
        var threshold = _aiSettings().AutoDeleteBelowScore;
        if (g.Origin == StrategyOrigin.Evolved && result.Score < threshold)
        {
            Notify($"🗑 ИИ: стратегия «{g.DisplayName}» ({result.Score}%) ниже порога {threshold}% — удалена автоматически.", result: result);
            TryDeleteGenomeBatFile(g);
            _registry.Remove(g.Id);
            _registry.Save();
            return true;
        }
        return false;
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
            var genome = GenomeParser.FromLaunchPlan(plan, name, StrategyOrigin.Builtin);
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

    private void SaveWarpConfig(StrategyGenome g)
    {
        try
        {
            var exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exported_configs");
            Directory.CreateDirectory(exportDir);

            var safeName = BatMaterializer.SanitizeFileName(g.DisplayName);
            var path = Path.Combine(exportDir, $"{safeName}.conf");

            if (!File.Exists(path))
            {
                File.WriteAllText(path, g.WarpConfig);
                Notify($"💾 Конфиг Warp «{g.DisplayName}» сохранён в exported_configs.");
            }
        }
        catch (Exception ex)
        {
            Notify($"❌ Ошибка сохранения конфига: {ex.Message}");
        }
    }

    private static void TryDeleteGenomeBatFile(StrategyGenome g)
    {
        try
        {
            if (!string.IsNullOrEmpty(g.SourceBatPath) && File.Exists(g.SourceBatPath))
                File.Delete(g.SourceBatPath);
        }
        catch
        {
        }
    }

    private List<TargetEntry> BuildTargets()
    {
        var targets = TargetEntry.ParseFile(_getTargetsPath());

        foreach (var site in EnabledSites)
        {
            if (ConnectivityChecker.BuiltinSites.TryGetValue(site, out var entries))
                targets.AddRange(entries);
        }

        targets.AddRange(UserSiteTargets);

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
