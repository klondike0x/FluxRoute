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
    private readonly SemaphoreSlim _aiGate = new(1, 1); // мьютекс: purge ∨ циклы ИИ выполняются исключающе
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

    public void SyncRegistryFromEngine()
    {
        SyncBuiltins();
        _evolver.GarbageCollectEvolved();
        _registry.Save();
    }

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

    public Task CheckNowAsync(CancellationToken ct = default) => RunCycleAsync(ct);
    [Obsolete("Use CheckNowAsync(CancellationToken) instead")]
    public Task CheckNowAsync_Legacy() => RunCycleAsync(CancellationToken.None);

    /// <summary>
    /// Точечная проверка ВЫБРАННОЙ (активно выбранной в интерфейсе) стратегии.
    /// В отличие от <see cref="CheckNowAsync"/> (полный цикл с переподбором/сменой/эволюцией)
    /// этот метод: не переподбирает, не переключает стратегию, не запускает эволюцию и
    /// не удаляет эволюции. Он только проверяет текущую стратегию, записывает результат
    /// (<see cref="StrategyGenome.LastVerificationScore"/>) в генотип и остаётся на месте (issue #89).
    /// </summary>
    public async Task ProbeSelectedStrategyAsync(CancellationToken ct = default)
    {
        var active = _getActiveProfile();
        if (active is null)
        {
            Notify("ИИ: нет выбранной стратегии для проверки.");
            return;
        }

        var genome = FindGenomeForProfile(active);
        if (genome is null)
        {
            Notify($"ИИ: для «{active.DisplayName}» нет записи генотипа — проверка пропущена.");
            return;
        }

        var fp = _fingerprints.Capture();
        _registry.MarkNetworkSeen(fp.Hash);
        _registry.Save();
        Notify($"ИИ: проверка выбранной стратегии «{genome.DisplayName}»...");
        try
        {
            await TryProbeAndPersistGenomeAsync(genome, fp, ct, isFreshlyEvolved: false, autoDeleteBelowThreshold: false)
                .ConfigureAwait(false);
        }
        finally
        {
            // ProbeAsync переключается на NEW ProfileItem, которого нет в коллекции Profiles —
            // возвращаем исходный выбранный профиль (правка по Codex P2, десятый раунд).
            if (active is not null && !ReferenceEquals(_getActiveProfile(), active))
                await _switchProfile(active).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Находит генотип, соответствующий выбранному профилю (по имени bat/отображаемому имени/пути).
    /// </summary>
    private StrategyGenome? FindGenomeForProfile(ProfileItem profile) =>
        _registry.GetGenomes().FirstOrDefault(g =>
            string.Equals(g.BatFileName, profile.FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(g.DisplayName, profile.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(g.SourceBatPath, profile.FullPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Полное сканирование всех включённых стратегий ИИ с сохранением результатов проверки.
    /// По умолчанию НЕ удаляет слабые эволюции — автоудаление вынесено в отдельное действие
    /// (<see cref="PurgeWeakEvolutionsAsync"/>), чтобы кнопка «Проверить сейчас» не чистила стратегии тайно (issue #89).
    /// </summary>
    public async Task ProbeAllEnabledStrategiesAsync(
        CancellationToken ct = default,
        bool autoDeleteBelowThreshold = false)
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
            Notify($"ИИ: проверка {list.Count} стратегий...");
            foreach (var g in list)
            {
                var deleted = await TryProbeAndPersistGenomeAsync(g, fp, ct, isFreshlyEvolved: false, autoDeleteBelowThreshold)
                    .ConfigureAwait(false);
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
        // Мьютекс с purge: стартовый переподбор материализует/применяет генотипы и не должен
        // выполняться одновременно с очисткой (release #76, P2).
        await _aiGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var fp = _fingerprints.Capture();
        _registry.MarkNetworkSeen(fp.Hash);
        _registry.Save();

        var pool = GenePool();
        if (pool.Count == 0)
        {
            Notify(_registry.GetGenomes().Count > 0
                ? "ИИ: нет включённых стратегий. Включите хотя бы одну на вкладке «Оркестратор»."
                : "ИИ: нет стратегий/genomes в engine/. Обновите Flowseal.");
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
        finally
        {
            _aiGate.Release();
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Взаимное исключение с очисткой эволюций (и другими циклами ИИ): purge и цикл не могут
        // выполняться одновременно — иначе цикл мог бы продолжать запись bandit/переключение,
        // пока purge удаляет геном/BAT (релизный PR #76, P1).
        await _aiGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var ai = _aiSettings();
        _history.RotateOldEntries(ai.KeepHistoryDays);

        var fp = _fingerprints.Capture();
        _registry.MarkNetworkSeen(fp.Hash);

        if (_networkDirty)
        {
            await RepickAfterNetworkChangeAsync(fp, ct).ConfigureAwait(false);
            _networkDirty = false;
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
        finally
        {
            _aiGate.Release();
        }
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
            Notify($"ИИ: не удалось материализовать стратегию для «{g.DisplayName}».");
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
            Notify("ИИ: эволюция завершена, новые стратегии в engine/ai-evolved/.");
            await VerifyEvolvedGenomeAsync(child, fp, ct).ConfigureAwait(false);
        }
        else
            Notify("ИИ: эволюция не дала новой стратегии (мало вариантов в пуле или нет родителей).");
    }

    public async Task EvolveNowAsync(CancellationToken ct = default)
    {
        // Мьютекс с purge: явная эволюция материализует/проверяет генотипы и не должна
        // выполняться одновременно с очисткой (release #76, P2).
        await _aiGate.WaitAsync(ct).ConfigureAwait(false);
        try
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
        finally
        {
            _aiGate.Release();
        }
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
        var deleted = false;
        try
        {
            await _refreshProfiles().ConfigureAwait(false);
            deleted = await TryProbeAndPersistGenomeAsync(child, fp, ct, isFreshlyEvolved: true, autoDeleteBelowThreshold: true).ConfigureAwait(false);
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
        bool isFreshlyEvolved, bool autoDeleteBelowThreshold = false)
    {
        var testProfile = ResolveProfile(g);
        if (testProfile is null)
        {
            Notify($"ИИ: не удалось проверить «{g.DisplayName}».");
            return false;
        }
        Notify(isFreshlyEvolved
            ? $"ИИ: проверка новой стратегии «{g.DisplayName}»..."
            : $"ИИ: проверка «{g.DisplayName}»...");
        var targets = BuildTargets();
        var probeOptions = new ProfileProbeOptions
        {
            StartupWait = TimeSpan.FromSeconds(5),
            StableWait = TimeSpan.FromSeconds(1.5),
            ProcessWaitTimeout = TimeSpan.FromSeconds(8),
            StopAfterProbe = false,
        };
        var result = await _probeService.ProbeAsync(testProfile, targets, probeOptions, ct).ConfigureAwait(false);

        // Если сеть сменилась за время пробы (ожидание старта/стабилизации/проверки целей) —
        // результат относится к другой сети и не должен быть записан под исходным хэшем:
        // иначе bandit/очистка получили бы наблюдение от чужой сети (правка по Codex P2, 12-й раунд).
        if (!string.Equals(_fingerprints.Capture().Hash, fp.Hash, StringComparison.Ordinal))
        {
            Notify($"⚠️ ИИ: сеть изменилась во время проверки «{g.DisplayName}» — результат не сохранён.");
            return false;
        }

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
        // Если генотип удалён очисткой во время проверки — не воскрешаем запись в реестре,
        // иначе после завершения проверки он вернул бы запись для уже удалённого файла
        // (правка по Codex P1, девятый раунд).
        if (_registry.GetById(g.Id) is null)
            return false;
        _registry.Upsert(g);
        _registry.Save();

        // ═══ АВТОУДАЛЕНИЕ НЕУДАЧНЫХ ЭВОЛЮЦИОНИРОВАННЫХ СТРАТЕГИЙ ═══
        // Срабатывает только при явно запрошенном автоудалении (проверка свежей эволюции/очистка).
        // «Проверить сейчас» больше не чистит стратегии тайно (issue #89).
        var threshold = _aiSettings().AutoDeleteBelowScore;
        if (autoDeleteBelowThreshold && g.Origin == StrategyOrigin.Evolved && result.Score < threshold)
        {
            // Проверяем, есть ли на этой сети хотя бы одна встроенная стратегия с Score >= threshold.
            // Если нет — сеть слишком агрессивна, удалять эволюцию несправедливо (исправление #62).
            var builtinOk = _history.LoadForNetwork(fp.Hash)
                .Where(o => _registry.GetById(o.GenomeId)?.Origin == StrategyOrigin.Builtin && o.Score >= threshold)
                .ToList();

            if (builtinOk.Count > 0)
            {
                Notify($"🗑 ИИ: стратегия «{g.DisplayName}» ({result.Score}%) ниже порога {threshold}% — удалена автоматически.", result: result);
                if (TryDeleteGenomeBatFile(g))
                {
                    _registry.Remove(g.Id);
                    _registry.Save();
                    return true;
                }
                // Файл не удалился — запись из реестра не убираем (Codex P2, 9-й раунд).
                Notify($"⚠️ ИИ: не удалось удалить файл «{g.DisplayName}» — стратегия сохранена.", result: result);
            }

            Notify($"🧬 ИИ: стратегия «{g.DisplayName}» ({result.Score}%) ниже порога {threshold}%, но оставлена — встроенные тоже не проходят (сеть агрессивна).", result: result);
        }
        return false;
    }

    /// <summary>
    /// Отдельное действие очистки: удаляет эволюционированные стратегии, у которых
    /// последняя проверка ниже порога <see cref="AiSettings.AutoDeleteBelowScore"/>.
    /// Гарантия #62: эволюция удаляется только если на этой сети есть проходящая встроенная
    /// стратегия (иначе сеть слишком агрессивна и удалять несправедливо).
    /// Итог «Проверить сейчас» и «Сканировать все стратегии» НЕ запускают это скрыто (issue #89).
    /// </summary>
    public async Task<int> PurgeWeakEvolutionsAsync(CancellationToken ct = default)
    {
        // Взаимное исключение с циклами ИИ: purge и фоновые циклы не могут выполняться
        // одновременно, иначе цикл мог продолжать запись bandit/переключение, пока очистка
        // удаляет геном/BAT (релизный PR #76, P1). Цикл ждёт, а не пропускает.
        await _aiGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var threshold = _aiSettings().AutoDeleteBelowScore;
            var fp = await Task.Run(() => _fingerprints.Capture(), ct).ConfigureAwait(false);
            _registry.MarkNetworkSeen(fp.Hash);
            _registry.Save();

        // Кандидаты — эволюции, чей ПОСЛЕДНИЙ результат именно на ТЕКУЩЕЙ сети ниже порога.
        // Не используем глобальный LastVerificationScore: он без привязки к сети, а порог защиты
        // #62 вычисляется для текущей сети — обе стороны сравнения должны быть на одной сети.
        var candidates = _registry.GetGenomes()
            .Where(g => g.Origin == StrategyOrigin.Evolved)
            .Select(g => (genome: g, score: GetLatestScoreOnNetwork(g, fp.Hash)))
            .Where(x => x.score is { } score && score < threshold)
            .Select(x => x.genome)
            .ToList();

        if (candidates.Count == 0)
        {
            Notify($"🗑 ИИ: нет слабых эволюций ниже порога {threshold}% на этой сети.");
            return 0;
        }

        // Проверяем, есть ли на этой сети хотя бы одна встроенная стратегия, чей ПОСЛЕДНИЙ
        // результат ≥ порога. Берём именно последний outcome каждой встроенной: устаревший
        // успех в прошлом не должен позволять удалять эволюции, если сейчас встроенная падает
        // (правка по Codex P1, третий раунд).
        var builtinOk = _history.LoadForNetwork(fp.Hash)
            .Where(o => _registry.GetById(o.GenomeId)?.Origin == StrategyOrigin.Builtin)
            .GroupBy(o => o.GenomeId)
            .Select(group => group.OrderByDescending(o => o.Timestamp).First())
            .Any(o => o.Score >= threshold);

        var deleted = 0;
        foreach (var g in candidates)
        {
            if (ct.IsCancellationRequested) break;

            var thisScore = GetLatestScoreOnNetwork(g, fp.Hash);
            if (thisScore is not { } score)
                continue; // нет данных именно на этой сети — не трогаем

            if (!builtinOk)
            {
                Notify($"🧬 ИИ: «{g.DisplayName}» ({score}%) ниже порога {threshold}%, но оставлена — встроенные тоже не проходят (сеть агрессивна).");
                break;
            }

            Notify($"🗑 ИИ: стратегия «{g.DisplayName}» ({score}%) ниже порога {threshold}% на этой сети — удалена.");
            if (!TryDeleteGenomeBatFile(g))
            {
                // Файл не удалился (занят/защищён) — запись из реестра не убираем,
                // иначе LoadProfiles() снова найдёт BAT, а реестр им уже не управляет (Codex P2, 9-й раунд).
                Notify($"⚠️ ИИ: не удалось удалить файл «{g.DisplayName}» — стратегия сохранена.");
                continue;
            }
            _registry.Remove(g.Id);
            deleted++;
        }

        _registry.Save();
        Notify($"🗑 ИИ: очистка завершена — удалено {deleted} слабых эволюций.");
        return deleted;
        }
        finally
        {
            _aiGate.Release();
        }
    }

    /// <summary>
    /// Возвращает последний результат проверки генотипа именно на указанной сети.
    /// Если на этой сети проверок не было — null.
    /// </summary>
    private int? GetLatestScoreOnNetwork(StrategyGenome g, string networkHash)
    {
        var last = _history.LoadFor(g.Id, networkHash)
            .OrderByDescending(o => o.Timestamp)
            .FirstOrDefault();
        return last?.Score;
    }

    /// <summary>
    /// Переносит результаты уже выполненного полного сканирования в генотипы ИИ:
    /// пишет LastVerificationScore/LastVerifiedAt, а также сетевой outcome в историю и
    /// в bandit-реестр, используя реальные данные проверки (ProcessStable/SuccessRate/
    /// FailedChecks), а не реконструируя их из композитного счёта (правка по Codex P2, 11-й раунд).
    /// Сетевой хэш захватывается ДО начала скана и передаётся сюда, чтобы при смене сети
    /// в процессе скана все результаты не были бы помечены новым (а не фактическим) хэшем.
    /// </summary>
    public void PersistScanVerification(IReadOnlyList<(Guid genomeId, ProfileProbeResult result)> results, string networkHash)
    {
        if (results.Count == 0)
            return;

        _registry.MarkNetworkSeen(networkHash);
        var updated = false;

        foreach (var (genomeId, result) in results)
        {
            var g = _registry.GetById(genomeId);
            if (g is null || result.Score < 0)
                continue;

            var failureSig = !result.ProcessStable
                ? "winws_failed"
                : result.Score < (int)Math.Round(FailThreshold * 100)
                    ? "network_failed"
                    : null;

            _history.Append(new ProbeOutcome
            {
                GenomeId = genomeId,
                NetworkHash = networkHash,
                Timestamp = DateTimeOffset.UtcNow,
                Score = result.Score,
                SuccessRate = result.SuccessRate,
                AvgLatencyMs = result.Checks.Where(c => c.ElapsedMs.HasValue).Select(c => c.ElapsedMs!.Value).DefaultIfEmpty(0).Average(),
                ProcessStable = result.ProcessStable,
                FailedTargetKeys = result.FailedChecks.Select(c => c.Key).ToList(),
                FailureSignature = failureSig,
            });

            if (result.IsWorking(FailThreshold))
            {
                _registry.RecordBanditSuccess(genomeId, networkHash);
                _bandit.RegisterSuccess(genomeId);
            }
            else
            {
                _registry.RecordBanditFailure(genomeId, networkHash);
                _bandit.RegisterFailure(g, failureSig);
            }

            g.LastVerificationScore = result.Score;
            g.LastVerifiedAt = DateTimeOffset.UtcNow;
            _registry.Upsert(g);
            updated = true;
        }

        if (updated)
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

    /// <summary>
    /// Пытается удалить BAT-файл генотипа. Возвращает true, если файл нигде не найден (удалять нечего)
    /// или успешно удалён; false — если файл существует, но удалить не удалось (занят/защищён).
    /// Учитывает, что сохранённый <see cref="StrategyGenome.SourceBatPath"/> может быть устаревшим:
    /// дополнительно проверяем актуальное расположение engine/ai-evolved/&lt;BatFileName&gt;.
    /// </summary>
    private bool TryDeleteGenomeBatFile(StrategyGenome g)
    {
        try
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(g.SourceBatPath))
                candidates.Add(g.SourceBatPath);
            if (!string.IsNullOrEmpty(g.BatFileName))
                candidates.Add(Path.Combine(_engineDir(), "ai-evolved", g.BatFileName));

            var existing = candidates.Where(File.Exists).ToList();
            if (existing.Count == 0)
                return true; // файла нигде нет — удалять нечего

            // Удаляем ВСЕ живые копии (путь мог сохраниться в нескольких местах при переносе),
            // а не только первую — иначе активная копия останется «бесхозной» (Codex P2, 11-й раунд).
            foreach (var path in existing)
                File.Delete(path);
            return true;
        }
        catch
        {
            return false; // файл занят/защищён — не удалился
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
