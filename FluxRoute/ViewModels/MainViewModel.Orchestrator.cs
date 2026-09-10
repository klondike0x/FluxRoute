using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;
using Application = System.Windows.Application;
using FluxRoute.Controls;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.AI.Models;
using FluxRoute.AI.Services;
using FluxRoute.AI.Stats;
using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public sealed partial class AiStrategyRowVm : ObservableObject
{
    private readonly AiStrategyRegistry _registry;
    private bool _suppress;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string OriginTag { get; }
    public bool CanDelete { get; }
    [ObservableProperty] private string wilsonText = "";
    [ObservableProperty] private string wilsonToolTip = "";
    [ObservableProperty] private string verificationText = "";
    [ObservableProperty] private string verificationToolTip = "";
    [ObservableProperty] private bool isEnabled;

    public AiStrategyRowVm(AiStrategyRegistry registry, StrategyGenome g, int successes, int trials, double wilsonLower)
    {
        _registry = registry;
        Id = g.Id;
        DisplayName = g.DisplayName;
        OriginTag = g.Origin == StrategyOrigin.Evolved ? "эволюция" : "встроенная";
        CanDelete = g.Origin == StrategyOrigin.Evolved;
        ApplyWilson(successes, trials, wilsonLower);
        ApplyVerification(g);
        _suppress = true;
        IsEnabled = g.OrchestratorEnabled;
        _suppress = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress)
            return;
        _registry.SetOrchestratorEnabled(Id, value);
    }

    private void ApplyWilson(int successes, int trials, double wilsonLower)
    {
        if (trials <= 0)
        {
            WilsonText = "—";
            WilsonToolTip =
                "История проверок пуста. Значение появится после ручной проверки, работы оркестратора или эволюции.";
            return;
        }
        WilsonText = $"{wilsonLower * 100:0.#}% ({successes}/{trials})";
        WilsonToolTip =
            $"Нижняя граница Уилсона: {wilsonLower * 100:0.#}% — консервативная оценка качества для подбора ИИ (чем выше, тем надёжнее).\n" +
            $"Успешных: {successes} из {trials} — доля проверок со счётом ≥50% (winws стабилен и цели доступны).";
    }

    private void ApplyVerification(StrategyGenome g)
    {
        if (g.LastVerificationScore is null || g.LastVerifiedAt is null)
        {
            VerificationText = "—";
            VerificationToolTip =
                "Последняя разовая проверка ещё не выполнялась (кнопка «Проверить сейчас» или автопроверка после эволюции).";
            return;
        }
        var t = g.LastVerifiedAt.Value.LocalDateTime;
        VerificationText = $"{g.LastVerificationScore}% · {t:HH:mm}";
        VerificationToolTip =
            $"Результат последней проверки: {g.LastVerificationScore}% — итоговый счёт по выбранным сайтам и стабильности winws.\n" +
            $"Время: {t:HH:mm} — когда завершилась последняя проверка этой стратегии ({t:dd.MM.yyyy}).";
    }
}

public sealed partial class ScanTargetCheckVm : ObservableObject
{
    public string Identity { get; }
    public string DisplayName { get; }
    public string TypeText { get; }

    [ObservableProperty] private string statusGlyph = "·";
    [ObservableProperty] private string resultText = "ожидание";
    [ObservableProperty] private string resultColor = "#66758E";
    [ObservableProperty] private string detailText = "Ожидает проверки";
    [ObservableProperty] private string elapsedText = "—";

    public ScanTargetCheckVm(TargetEntry target)
    {
        Identity = $"{target.Key}|{target.Value}";
        DisplayName = GetDisplayName(target);
        TypeText = target.Kind == TargetKind.Ping ? "PING" : "САЙТ";
    }

    public void Reset()
    {
        StatusGlyph = "·";
        ResultText = "ожидание";
        ResultColor = "#66758E";
        DetailText = "Ожидает проверки";
        ElapsedText = "—";
    }

    public void SetResult(CheckResult result)
    {
        var ok = result.Ok;
        StatusGlyph = ok ? "✓" : "×";
        ResultText = ok ? "OK" : "сбой";
        ResultColor = ok ? "#38D9A9" : "#FF6B6B";
        DetailText = string.IsNullOrWhiteSpace(result.Detail)
            ? (ok ? "Цель доступна" : "Цель недоступна")
            : result.Detail;
        ElapsedText = result.ElapsedMs is { } elapsed ? $"{elapsed} мс" : "—";
    }

    private static string GetDisplayName(TargetEntry target)
    {
        if (target.Kind == TargetKind.Ping)
            return target.Value;

        if (Uri.TryCreate(target.Value, UriKind.Absolute, out var uri))
            return uri.Host;

        return string.IsNullOrWhiteSpace(target.Key) ? target.Value : target.Key;
    }
}
public partial class MainViewModel
{
    private const int MaxLogEntries = 50;
    public ObservableCollection<ScanTargetCheckVm> ScanTargetChecks { get; } = new();
    public ObservableCollection<ProfileScore> ScanPassedProfiles { get; } = new();
    [ObservableProperty] private string scanPassedSummary = "Пока ни одна стратегия не прошла проверку.";


    private void ResetScanTargetChecks(IEnumerable<TargetEntry> targets)
    {
        ScanTargetChecks.Clear();
        foreach (var target in targets)
            ScanTargetChecks.Add(new ScanTargetCheckVm(target));
    }

    private void ResetCurrentScanTargetChecks()
    {
        foreach (var target in ScanTargetChecks)
            target.Reset();
    }

    private void UpdateScanTargetCheck(CheckResult result)
    {
        var identity = $"{result.Key}|{result.Value}";
        var target = ScanTargetChecks.FirstOrDefault(x =>
            string.Equals(x.Identity, identity, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(x.DisplayName, result.Value, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(x.TypeText, result.Kind == TargetKind.Ping ? "PING" : "САЙТ", StringComparison.OrdinalIgnoreCase)));
        target?.SetResult(result);
    }

    private void UpdateScanBestStrategyText()
    {
        var top = ProfileScores.Where(s => s.Score > 0).OrderByDescending(s => s.Score).FirstOrDefault();
        ScanBestStrategyText = top is null
            ? "Рабочая стратегия не найдена"
            : $"{top.DisplayName} · {top.ScoreText}";
    }

    private void RebuildPassedScanProfiles()
    {
        ScanPassedProfiles.Clear();
        foreach (var score in ProfileScores.Where(x => x.Score > 0))
            ScanPassedProfiles.Add(score);

        ScanPassedSummary = ScanPassedProfiles.Count == 0
            ? "Пока ни одна стратегия не прошла проверку."
            : $"Прошли проверку: {ScanPassedProfiles.Count}";
    }
    private void AddOrchestratorLog(string message)
    {
        OrchestratorLogs.Add(message);
        while (OrchestratorLogs.Count > MaxLogEntries)
            OrchestratorLogs.RemoveAt(0);
    }

    private void OnOrchestratorStatus(object? sender, OrchestratorEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        // Важно: не используем Dispatcher.Invoke(). Во время сканирования стратегией события идут
        // из фоновых задач и из UI-потока вперемешку; синхронный Invoke может создать re-entrancy.
        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                AddOrchestratorLog(e.Message);
                OrchestratorStatus = e.Message;

                if (e.ProbeResult is not null && IsScanning && e.ProbeResult.Profile is not null)
                {
                    var liveScore = ProfileScores.FirstOrDefault(s =>
                        s.FileName == e.ProbeResult.Profile.FileName);
                    liveScore?.SetProbeResult(e.ProbeResult);
                    foreach (var check in e.ProbeResult.Checks)
                        UpdateScanTargetCheck(check);
                    RebuildPassedScanProfiles();
                    ScanCurrentStrategy = e.ProbeResult.ProfileName;
                    ScanCurrentResultText = $"{e.ProbeResult.Score}% · {e.ProbeResult.Summary}";
                }

                if (e.Message.Contains("Сканирование завершено", StringComparison.OrdinalIgnoreCase))
                {
                    SortProfileScores();
                    SaveSettings();
                }

                // ═══ v1.6.0: Toast-уведомления при создании новых стратегий ═══
                if (e.Message.Contains("ИИ: эволюция завершена", StringComparison.OrdinalIgnoreCase) ||
                    e.Message.Contains("ИИ: проверка новой стратегии", StringComparison.OrdinalIgnoreCase))
                {
                    _trayIcon?.ShowBalloon("FluxRoute — ИИ", e.Message.Replace($"[{DateTime.Now:HH:mm:ss}] ", ""));
                }

                if (e.IsSwitched && e.NewProfile is not null)
                {
                    var profile = Profiles.FirstOrDefault(p => p.DisplayName == e.NewProfile);
                    if (profile is not null)
                    {
                        SelectedProfile = profile;
                        CurrentStrategy = profile.DisplayName;
                        Logs.Add($"[Оркестратор] Переключено на «{profile.DisplayName}»");
                        ProfileSwitchNotification?.Invoke(this, profile.DisplayName);
                    }
                }

                if (e.ProbeResult is not null && e.Message.Contains("ИИ:", StringComparison.Ordinal))
                {
                    RefreshAiDashboard();
                    RebuildAiStrategyRows();
                }
            }
            catch (Exception ex)
            {
                Logs.Add($"[Оркестратор] Ошибка UI-обновления: {ex.Message}");
            }
        }));
    }

    private void UpdateOrchestratorNextCheck()
    {
        DateTimeOffset? next = null;
        if (_aiOrchestrator.IsRunning)
            next = _aiOrchestrator.NextCheckAt;
        else if (_orchestrator.IsRunning)
            next = _orchestrator.NextCheckAt;

        if (next is { } n)
        {
            var remaining = n - DateTimeOffset.Now;
            OrchestratorNextCheck = remaining > TimeSpan.Zero
                ? $"через {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}"
                : "сейчас...";
        }
        else
        {
            OrchestratorNextCheck = "—";
        }
    }

    /// <summary>
    /// Запускает полное сканирование стратегий после завершения онбординга.
    /// Проверяются только цели, выбранные пользователем на втором шаге.
    /// </summary>
    public async Task RunInitialProfileCheckAsync()
    {
        if (string.Equals(_selectedComponent, "none", StringComparison.OrdinalIgnoreCase))
        {
            Logs.Add("[Онбординг] Основной компонент отключён — проверка стратегий пропущена.");
            return;
        }

        var targetSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SiteYouTube) targetSites.Add("YouTube");
        if (SiteDiscord) targetSites.Add("Discord");

        if (targetSites.Count == 0)
        {
            Logs.Add("[Онбординг] Нет целей для первичной проверки.");
            return;
        }

        var targetNames = string.Join(" и ", targetSites.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Logs.Add($"[Онбординг] Запускаю сканирование стратегий для целей: {targetNames}…");
        AddToRecentLogs($"🔍 Сканирование стратегий: {targetNames}");

        try
        {
            await ScanProfilesAsync(targetSites);
            AddToRecentLogs("✅ Сканирование стратегий завершено");
        }
        catch (Exception ex)
        {
            Logs.Add($"[Онбординг] Ошибка сканирования стратегий: {ex.Message}");
            AddToRecentLogs("❌ Сканирование стратегий завершилось с ошибкой");
        }
        finally
        {
            // Возвращаем обычный набор целей оркестратора после первичной проверки.
            UpdateOrchestratorEnabledSites();
        }
    }
    private async Task SwitchProfileAsync(ProfileItem? profile)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        void SwitchOnUi()
        {
            _suppressOrchestratorStop = true;
            Stop();
            _suppressOrchestratorStop = false;
            if (profile is not null)
            {
                _suppressProfileWarning = true;
                SelectedProfile = profile;
                _suppressProfileWarning = false;
            }
        }

        if (dispatcher.CheckAccess())
            SwitchOnUi();
        else
            await dispatcher.InvokeAsync(SwitchOnUi).Task.ConfigureAwait(false);

        // Даём время на завершение WinDivert и winws.exe
        await Task.Delay(1500).ConfigureAwait(false);

        void StartOnUi()
        {
            if (profile is not null)
                Start();
        }

        if (dispatcher.CheckAccess())
            StartOnUi();
        else
            await dispatcher.InvokeAsync(StartOnUi).Task.ConfigureAwait(false);
    }

    private Task UpdateProfileScoreAsync(string fileName, int score)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        void UpdateOnUi()
        {
            var entry = ProfileScores.FirstOrDefault(s => s.FileName == fileName);
            if (entry is null)
                return;
            if (score == -1)
                entry.SetPending();
            else
                entry.SetScore(score / 100.0);
            OnPropertyChanged(nameof(NeedsInitialProfileScan));
        }

        if (dispatcher.CheckAccess())
        {
            UpdateOnUi();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(UpdateOnUi).Task;
    }

    private void RebuildProfileScores()
    {
        ProfileScores.Clear();
        foreach (var p in Profiles)
            ProfileScores.Add(new ProfileScore { DisplayName = p.DisplayName, FileName = p.FileName });
        OnPropertyChanged(nameof(NeedsInitialProfileScan));
    }

    private void SortProfileScores()
    {
        var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
        ProfileScores.Clear();
        foreach (var s in sorted)
            ProfileScores.Add(s);
    }

    public void RebuildAiStrategyRows()
    {
        try
        {
            AiStrategyRows.Clear();
            var list = _aiRegistry.GetGenomes().ToList();
            var evolved = list.Where(x => x.Origin == StrategyOrigin.Evolved).OrderByDescending(x => x.Generation).ToList();
            var builtin = list.Where(x => x.Origin != StrategyOrigin.Evolved)
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var g in evolved)
            {
                var (succ, trials, w) = WilsonStatsForGenome(g);
                AiStrategyRows.Add(new AiStrategyRowVm(_aiRegistry, g, succ, trials, w));
            }

            foreach (var g in builtin)
            {
                var (succ, trials, w) = WilsonStatsForGenome(g);
                AiStrategyRows.Add(new AiStrategyRowVm(_aiRegistry, g, succ, trials, w));
            }

            OnPropertyChanged(nameof(AiStrategyRowCount));
        }
        catch
        {
        }
    }

    /// <summary>
    /// v1.7.1: Переносит результаты уже выполненного полного сканирования в генотипы ИИ
    /// (LastVerificationScore/LastVerifiedAt) и в сетевую историю/бандит с реальными данными
    /// проверки, чтобы вкладка ИИ и очистка/подбор видели фактические значения.
    /// Не перезапускает стратегии. <paramref name="networkHash"/> — хэш сети ДО начала скана.
    /// </summary>
    private void PersistScanScoresIntoGenomes(string networkHash)
    {
        try
        {
            var results = new List<(Guid genomeId, ProfileProbeResult result)>();
            var lastScan = _orchestrator.LastScanResults;
            foreach (var g in _aiRegistry.GetGenomes().ToList())
            {
                var entry = lastScan.FirstOrDefault(e => e.result is not null &&
                    (string.Equals(e.profile.FileName, g.BatFileName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(e.profile.DisplayName, g.DisplayName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(e.profile.FullPath, g.SourceBatPath, StringComparison.OrdinalIgnoreCase)));
                if (entry.result is null || entry.result.Score < 0)
                    continue;

                results.Add((g.Id, entry.result));
            }

            // Мьютекс уже удерживает вызывающий (весь скан-и-импорт атомарен относительно циклов
            // ИИ — релизный PR #76, P2), поэтому вызываем ядро импорта БЕЗ повторного захвата:
            // SemaphoreSlim не реентерабелен, вложенный захват привёл бы к дедлоку.
            _aiOrchestrator.ApplyScanResults(results, networkHash);
        }
        catch (Exception ex)
        {
            Logs.Add($"[ИИ] Ошибка переноса результатов скана в генотипы: {ex.Message}");
        }
    }

    private Task EnsureProtectionRunningAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        void EnsureOnUi()
        {
            if (SelectedProfile is not null && !IsTrackedProcessRunning())
                Start();
        }

        if (dispatcher.CheckAccess())
        {
            EnsureOnUi();
            return Task.CompletedTask;
        }
        return dispatcher.InvokeAsync(EnsureOnUi).Task;
    }

    private (int successes, int trials, double wilsonLower) WilsonStatsForGenome(StrategyGenome g)
    {
        var outcomes = _aiHistoryStore.LoadAll().Where(o => o.GenomeId == g.Id).ToList();
        var succ = outcomes.Count(o => o.Score >= 50);
        var trials = outcomes.Count;
        var w = WilsonScore.LowerBound(succ, trials);
        return (succ, trials, w);
    }

    [RelayCommand]
    private void DeleteAiStrategy(AiStrategyRowVm? row)
    {
        if (row is null)
            return;

        var g = _aiRegistry.GetById(row.Id);
        if (g is null)
        {
            RebuildAiStrategyRows();
            return;
        }

        if (g.Origin != StrategyOrigin.Evolved)
        {
            Logs.Add("[ИИ] Встроенные стратегии из engine/ нельзя удалить, снимите галочку.");
            return;
        }

        if (!CustomDialog.Show(
            "Удалить стратегию",
            $"Удалить «{g.DisplayName}» и файл из ai-evolved?",
            "Удалить",
            "Отмена",
            isDanger: true))
            return;

        var deletedPath = g.SourceBatPath;
        var deletedFileName = g.BatFileName;

        TryDeleteGenomeBatFile(g);
        _aiRegistry.Remove(g.Id);
        _aiRegistry.Save();

        var wasActive = SelectedProfile is not null &&
            (string.Equals(SelectedProfile.FileName, deletedFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SelectedProfile.FullPath, deletedPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SelectedProfile.DisplayName, g.DisplayName, StringComparison.OrdinalIgnoreCase));

        LoadProfiles();

        if (wasActive)
        {
            if (IsRunning)
                Stop();
            SelectedProfile = Profiles.FirstOrDefault();
        }

        RebuildAiStrategyRows();
        RefreshAiDashboard();
        Logs.Add($"[ИИ] Удалена стратегия «{g.DisplayName}».");
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

    /// <summary>Синхронизирует активные сайты с оркестратором без полной перезагрузки.
    /// Вызывается при изменении чекбоксов сайтов в реальном времени.</summary>
    private void SyncOrchestratorSites()
    {
        if (!_settingsLoaded) return;
        UpdateOrchestratorEnabledSites();
    }

    private void UpdateOrchestratorEnabledSites(IReadOnlySet<string>? siteOverride = null)
    {
        var sites = siteOverride is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(siteOverride, StringComparer.OrdinalIgnoreCase);

        if (siteOverride is null)
        {
            if (SiteYouTube) sites.Add("YouTube");
            if (SiteDiscord) sites.Add("Discord");
            if (SiteGoogle) sites.Add("Google");
            if (SiteTwitch) sites.Add("Twitch");
            if (SiteInstagram) sites.Add("Instagram");
            if (SiteTelegram) sites.Add("Telegram");
            if (SiteTikTok) sites.Add("TikTok");
        }
        _orchestrator.EnabledSites = sites;
        _aiOrchestrator.EnabledSites = sites;

        var userTargets = new List<TargetEntry>();
        // Берем домены из менеджера, ИСКЛЮЧАЯ те, что во вкладке "Исключения"
        foreach (var domain in CustomTargetDomains)
        {
            if (!string.IsNullOrWhiteSpace(domain) &&
                !CustomExcludeDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                userTargets.Add(BuildUserTargetEntry(domain));
            }
        }

        // Fallback на старый TextBox
        var legacyTargets = UserCustomSitesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("!") &&
                        !CustomExcludeDomains.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Select(s => BuildUserTargetEntry(s));

        foreach (var t in legacyTargets)
        {
            if (!userTargets.Any(x => x.Key == t.Key))
                userTargets.Add(t);
        }

        _orchestrator.UserSiteTargets = userTargets;
        _aiOrchestrator.UserSiteTargets = userTargets;
    }

    private static TargetEntry BuildUserTargetEntry(string raw)
    {
        var url = raw.Contains("://") ? raw : $"https://{raw}";
        return new TargetEntry
        {
            Key = raw,
            Kind = TargetKind.Http,
            Value = url
        };
    }

    // ── CancellationTokenSource для отмены сканирования ──
    private CancellationTokenSource? _scanCts;
    private StrategyScanWindow? _scanWindow;
    private int _scanGeneration;

    // ── Статус сканирования (для ScanProgressView) ──
    [ObservableProperty] private string scanStatusText = "";
    [ObservableProperty] private string scanTimeRemaining = "";
    [ObservableProperty] private string scanElapsed = "";
    private DateTime _scanStartTime;
    private int _scanTotalCount;
    private int _scanCurrentCount;
    private System.Windows.Threading.DispatcherTimer? _scanEtaTimer;

    [ObservableProperty] private string scanTargetsText = "Подготовка целей...";
    [ObservableProperty] private string scanCurrentStrategy = "Подготовка...";
    [ObservableProperty] private string scanCurrentResultText = "Ожидаю первый результат...";
    [ObservableProperty] private string scanBestStrategyText = "Пока нет результата";
    [ObservableProperty] private bool hasScanResult;

    /// <summary>
    /// Показывает, что для работы оркестратора ещё не построен рейтинг стратегий.
    /// </summary>
    public bool NeedsInitialProfileScan =>
        Profiles.Count > 0 && !IsScanning && !ProfileScores.Any(score => score.Score > 0);

    public bool CanViewScanResult => HasScanResult && !IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanViewScanResult));
        OnPropertyChanged(nameof(NeedsInitialProfileScan));
    }

    partial void OnHasScanResultChanged(bool value)
    {
        OnPropertyChanged(nameof(CanViewScanResult));
    }

    /// <summary>Признак, что сканирование можно отменить (показываем кнопку "Остановить").</summary>
    public bool CanCancelScan => IsScanning;

    private StrategyScanWindow CreateScanWindow()
    {
        var window = new StrategyScanWindow { DataContext = this };
        if (Application.Current.MainWindow is { IsLoaded: true } owner)
            window.Owner = owner;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_scanWindow, window))
                _scanWindow = null;
        };
        return window;
    }

    [RelayCommand]
    private void ViewScanResult()
    {
        if (!HasScanResult)
            return;

        if (_scanWindow is { IsVisible: true } visibleWindow)
        {
            if (visibleWindow.WindowState == System.Windows.WindowState.Minimized)
                visibleWindow.WindowState = System.Windows.WindowState.Normal;
            visibleWindow.Activate();
            return;
        }

        _scanWindow = CreateScanWindow();
        _scanWindow.Show();
    }
    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        _scanEtaTimer?.Stop();
        _scanEtaTimer = null;
        // Инвалидируем поколение — старый finally увидит gen != _scanGeneration и не тронет IsScanning.
        // Флаг подавления остановки снимаем прямо здесь: иначе после отмены скана «снаружи»
        // (остановка защиты) его finally уже не выполнит свою ветку и флаг остался бы включённым
        // навсегда — кнопка Stop больше не останавливала бы сервисы оркестратора
        // (правка по Codex P2, ревью #97).
        _scanGeneration++;
        _suppressOrchestratorStop = false;
        IsScanning = false;
        GlobalOverlayVisible = false;
        OnPropertyChanged(nameof(CanCancelScan));
        RebuildPassedScanProfiles();
        UpdateScanBestStrategyText();
        HasScanResult = true;
        ScanStatusText = "⏹ Сканирование остановлено";
        ScanProgressText = _scanTotalCount > 0
            ? $"Остановлено на {_scanCurrentCount}/{_scanTotalCount}"
            : "Сканирование остановлено";
        ScanTimeRemaining = "⏹ Остановлено";
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⏹ Сканирование остановлено пользователем.");
    }

    /// <summary>Обновляет оставшееся и прошедшее время на оверлее.</summary>
    private void UpdateScanEta()
    {
        var elapsed = DateTime.Now - _scanStartTime;
        ScanElapsed = $"Прошло: {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        if (_scanCurrentCount > 0 && _scanTotalCount > _scanCurrentCount)
        {
            var avgPerItem = elapsed.TotalSeconds / _scanCurrentCount;
            var remaining = (int)(avgPerItem * (_scanTotalCount - _scanCurrentCount));
            ScanTimeRemaining = remaining > 60
                ? $"Осталось: ~{remaining / 60} мин {remaining % 60} сек"
                : $"Осталось: ~{remaining} сек";
        }
        else
        {
            ScanTimeRemaining = "Осталось: —";
        }
    }

    private string BuildScanTargetsText()
    {
        var sites = _orchestrator.EnabledSites
            .OrderBy(site => site, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var customCount = _orchestrator.UserSiteTargets.Count;
        var targetText = sites.Count == 0 ? "цели не выбраны" : string.Join(" · ", sites);
        return customCount > 0 ? $"{targetText} · своих доменов: {customCount}" : targetText;
    }

    [RelayCommand]
    private Task ScanProfiles() => ScanProfilesAsync(null);

    private async Task ScanProfilesAsync(IReadOnlySet<string>? targetOverride)
    {
        if (IsScanning)
            return;
        // Счётчик поколений: только последний вызов сбрасывает IsScanning в finally
        var gen = ++_scanGeneration;

        // Отменяем предыдущее сканирование, если было
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var scanCt = _scanCts.Token;

        _orchestrator.ClearRankedProfiles();
        RebuildProfileScores();
        UpdateOrchestratorEnabledSites(targetOverride);
        ResetScanTargetChecks(_orchestrator.GetScanTargets());
        ScanPassedProfiles.Clear();
        ScanPassedSummary = "Пока ни одна стратегия не прошла проверку.";
        HasScanResult = false;
        IsScanning = true;
        GlobalOverlayVisible = false;
        if (_scanWindow is { IsVisible: true } oldWindow)
            oldWindow.Close();
        _scanWindow = CreateScanWindow();
        _scanWindow.Show();
        OnPropertyChanged(nameof(CanCancelScan));
        ScanStatusText = "Подготовка...";
        ScanProgressText = "Сканирование...";
        ScanProgressValue = 0;
        ScanTimeRemaining = "";
        ScanElapsed = "";
        ScanTargetsText = "Подготовка целей...";
        ScanCurrentStrategy = "Подготовка...";
        ScanCurrentResultText = "Ожидаю первый результат...";
        ScanBestStrategyText = "Пока нет результата";
        _scanStartTime = DateTime.Now;
        _scanTotalCount = 0;
        _scanCurrentCount = 0;

        // Таймер для обновления ETA и прошедшего времени (каждую секунду)
        _scanEtaTimer?.Stop();
        _scanEtaTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
        { Interval = TimeSpan.FromSeconds(1) };
        _scanEtaTimer.Tick += (_, _) => UpdateScanEta();
        _scanEtaTimer.Start();

        ScanTargetsText = BuildScanTargetsText();
        var wasRunning = IsTrackedProcessRunning();

        // Определяем общее количество стратегий для ETA
        _scanTotalCount = Profiles.Count;

        var checkProgress = new Progress<CheckResult>(UpdateScanTargetCheck);

        var progress = new Progress<(int current, int total)>(report =>
        {
            _scanCurrentCount = report.current;
            _scanTotalCount = report.total;
            var percent = report.total > 0
                ? (double)report.current / report.total * 100
                : 0;
            ScanProgressValue = percent;
            ScanStatusText = $"[{report.current}/{report.total}] Тестирую стратегии...";
            ScanProgressText = $"Сканирование... {report.current}/{report.total}";
            ResetCurrentScanTargetChecks();
            ScanCurrentStrategy = report.current > 0 && report.current <= Profiles.Count
                ? Profiles[report.current - 1].DisplayName
                : "Проверка следующей стратегии...";
            ScanCurrentResultText = "Проверяю стабильность winws и доступность целей...";
            UpdateScanEta();
        });

        try
        {
            // ═══ Флаг подавления остановки ставится ВНУТРИ сериализованного блока, уже после захвата
            // мьютекса. Ожидание в очереди за долгим циклом ИИ — это ещё не скан, и если пользователь
            // жмёт Stop в этот момент, остановка сервисов оркестратора должна пройти штатно, а не быть
            // подавлена: иначе процесс убит, а сервисы ИИ живы, и вставший из очереди скан снова
            // поднял бы профили после явной остановки защиты (правка по Codex P2, ревью #97).
            // Поднятый ранее скан снимается отменой в StopOrchestratorServices.
            var networkUnchangedAfterScan = false;
            ProfileItem? bestProfile = null;
            var bestScore = 0;
            var bestProfileStarted = false;

            // ═══ Полный скан и импорт его результатов в генотипы ИИ — под ОДНИМ удержанием
            // мьютекса с фоновыми циклами ИИ. Иначе RunCycleAsync успевал бы переключать/пробовать
            // профили, пока скан их измеряет, и в bandit/историю попадали бы баллы за уже нарушенные
            // профили. Сериализовать только запись мало — сериализуется весь скан-и-импорт
            // (правка по Codex, релизный PR #76, P2).
            // scanCt передаётся и в ожидание мьютекса: если скан отменят, пока он стоит в очереди за
            // циклом ИИ, выход должен идти через внешний путь отмены, а не через «успешное»
            // завершение уже отменённого скана (правка по Codex P2, ревью #97).
            await _aiOrchestrator.RunSerializedAsync(async () =>
            {
                _suppressOrchestratorStop = true;

                // ═══ v1.7.1: сетевой хэш фиксируем непосредственно перед сканом, уже удерживая
                // мьютекс, — тогда два замера ограничивают сам скан, а не время ожидания в очереди
                // за циклом ИИ (правка по Codex P2, ревью #97; ранее — P1, пятый раунд).
                var scanNetworkHash = _aiFingerprints.Capture().Hash;

                await _orchestrator.ScanAllProfilesAsync(scanCt, progress, checkProgress).ConfigureAwait(true);

                // Если сеть сменилась за время скана — результаты относятся к разным сетям и не
                // должны быть помечены одним хэшем (правка по Codex P1, восьмой раунд).
                networkUnchangedAfterScan = string.Equals(_aiFingerprints.Capture().Hash, scanNetworkHash, StringComparison.Ordinal);

                // Импорт идёт сразу, в том же удержании мьютекса — окна для цикла между измерением
                // и записью нет. При отмене скана LastScanResults может содержать результаты
                // ПРЕДЫДУЩЕГО скана — не переносим их под новым хэшем (Codex P1, 13-й раунд).
                if (AiEnabled && !scanCt.IsCancellationRequested && networkUnchangedAfterScan)
                    PersistScanScoresIntoGenomes(scanNetworkHash);

                // ═══ Финальный выбор и запуск лучшей стратегии — тоже под мьютексом:
                // ScanAllProfilesAsync останавливает последний проверенный профиль, но оставляет его
                // выбранным, поэтому цикл ИИ, ворвавшийся сразу после Release, проверил бы
                // остановленный процесс и записал фейл в свой _currentGenome, а UI параллельно
                // переключил бы профиль — состояние генотипа разошлось бы с рабочим профилем
                // (правка по Codex P1, ревью #97).
                bestProfile = _orchestrator.BestRankedProfile;
                bestScore = _orchestrator.BestRankedScore;
                if (bestProfile is not null)
                {
                    bestProfileStarted = true;
                    await SwitchProfileAsync(bestProfile).ConfigureAwait(false);
                }
                else if (wasRunning && SelectedProfile is not null && !IsTrackedProcessRunning())
                {
                    bestProfileStarted = true;
                    await EnsureProtectionRunningAsync().ConfigureAwait(false);
                }

                // ═══ Состояние ИИ согласуем ПОД ТЕМ ЖЕ удержанием мьютекса и во всех случаях: рабочим
                // становится либо лучший профиль скана, либо последний проверенный (фолбэк, когда ни
                // один профиль не набрал очков), а скан в любом случае оставляет выбранным последний
                // проверенный профиль. Если отслеживаемый генотип остался от прежнего профиля,
                // следующий цикл проверит новый профиль, а результат запишет под чужим Id — порча
                // истории и состояния бандита (правка по Codex P1, ревью #97).
                _aiOrchestrator.ReconcileGenomeWithActiveProfile();
            }, scanCt);

            SortProfileScores();
            RebuildPassedScanProfiles();
            UpdateScanBestStrategyText();
            HasScanResult = true;
            ScanStatusText = "Сканирование завершено";
            ScanProgressText = "Сканирование завершено";
            ScanProgressValue = 100;
            ScanTimeRemaining = "✅ Завершено";
            SaveSettings();

            // ═══ v1.7.1: в режиме ИИ «Сканировать все стратегии» обновляет и ИИ-строки
            // (генотипы), иначе данные о стратегиях на вкладке ИИ оставались с «—» (issue #89).
            // Первый полный скан (ScanAllProfilesAsync) уже проверил все профили — переносим
            // готовые результаты в генотипы БЕЗ повторного запуска стратегий (правка по Codex P2).
            if (AiEnabled)
            {
                // Скан и импорт уже выполнены в одном удержании мьютекса выше; здесь остаётся
                // только предупредить, если сеть сменилась и результаты сознательно не сохранены
                // (правка по Codex P1, восьмой раунд).
                if (!scanCt.IsCancellationRequested && !networkUnchangedAfterScan)
                {
                    var msg = "Сеть изменилась во время сканирования — результаты не сохранены.";
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⚠️ {msg}");
                    Logs.Add($"[ИИ] {msg}");
                }
                RebuildAiStrategyRows();
                RefreshAiDashboard();
            }

            // Профиль уже выбран и запущен внутри сериализованного блока; здесь только UI-часть:
            // AddOrchestratorLog/Logs пишут в привязанные коллекции и должны идти с UI-потока.
            ScanBestStrategyText = bestProfile is null
                ? "Рабочая стратегия не найдена"
                : $"{bestProfile.DisplayName} · {bestScore}%";
            if (bestProfileStarted && bestProfile is not null)
            {
                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ▶ Запуск лучшей стратегии «{bestProfile.DisplayName}» ({bestScore}%).");
                Logs.Add($"[Оркестратор] Лучшая стратегия после сканирования: «{bestProfile.DisplayName}».");
            }

            // Задержка перед закрытием оверлея, чтобы пользователь увидел "100% Завершено"
            await Task.Delay(800, scanCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (gen == _scanGeneration)
            {
                ScanStatusText = "⏹ Сканирование отменено";
                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⏹ Сканирование отменено.");
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (gen == _scanGeneration)
            {
                RebuildPassedScanProfiles();
                UpdateScanBestStrategyText();
                HasScanResult = true;
                ScanStatusText = $"❌ Ошибка: {ex.Message}";
                ScanProgressText = "Ошибка сканирования";
                ScanProgressValue = 0;
                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка сканирования: {ex.Message}");
                Logs.Add($"[Оркестратор] Ошибка сканирования: {ex.Message}");
                await Task.Delay(1500).ConfigureAwait(false);
            }
        }
        finally
        {
            if (gen == _scanGeneration)
            {
                _scanEtaTimer?.Stop();
                _scanEtaTimer = null;
                _suppressOrchestratorStop = false;
                IsScanning = false;
                GlobalOverlayVisible = false;
                OnPropertyChanged(nameof(CanCancelScan));
                _ = Task.Delay(1500).ContinueWith(_ =>
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => ScanProgressValue = 0),
                        System.Windows.Threading.DispatcherPriority.Background);
                });
            }
        }
    }

    // ── Запуск сервисов оркестратора (вызывается только когда Zapret уже работает) ──
    private bool _orchestratorStartInProgress;

    private async Task StartOrchestratorServicesAsync()
    {
        if (_orchestrator.IsRunning || _aiOrchestrator.IsRunning || _orchestratorStartInProgress)
            return;

        _orchestratorStartInProgress = true;
        try
        {
            if (int.TryParse(OrchestratorInterval, out var mins) && mins >= 1)
            {
                var interval = TimeSpan.FromMinutes(mins);
                _orchestrator.CheckInterval = interval;
                _aiOrchestrator.CheckInterval = interval;
            }

            UpdateOrchestratorEnabledSites();

            if (ProfileScores.Count == 0 || ProfileScores.All(s => s.Score == 0))
                RebuildProfileScores();

            if (NeedsInitialProfileScan)
            {
                AddOrchestratorLog("ℹ️ Первый запуск оркестратора: открываю проверку всех стратегий...");
                Logs.Add("[Оркестратор] Первичная проверка стратегий — сначала проверяю все стратегии.");
                await ScanProfilesAsync(null);

                if (!HasScanResult)
                {
                    _orchestrator.ClearRankedProfiles();
                    Logs.Add("[Оркестратор] Проверка была отменена — повторю её автоматически в фоне.");
                }
            }
            else if (IsScanning)
            {
                AddOrchestratorLog("ℹ️ Ожидаю завершения текущей проверки стратегий перед запуском оркестратора...");
                while (IsScanning && OrchestratorEnabled)
                    await Task.Delay(100);
            }

            if (!OrchestratorEnabled || !IsRunning)
                return;

            if (AiEnabled)
            {
                _aiOrchestrator.Start();
            }
            else
            {
                _orchestrator.Start();
            }

            OrchestratorRunning = true;
            StartProcessMonitor();
            Logs.Add("[Оркестратор] Запущен в автоматическом режиме.");
        }
        finally
        {
            _orchestratorStartInProgress = false;
        }
    }

    // ── Остановка сервисов оркестратора без изменения флага OrchestratorEnabled ──
    private void StopOrchestratorServices()
    {
        // ═══ Скан оркестратора держит мьютекс ИИ и после остановки сервисов поднял бы профили заново,
        // поэтому снимаем и выполняющийся скан, и стоящий в очереди за циклом ИИ. Прежнее условие
        // (_orchestratorStartInProgress) не покрывало скан, запущенный кнопкой: нажатие Stop убивало
        // процесс, но скан оставался жив и после отпускания мьютекса снова стартовал профили
        // (правка по Codex P2, ревью #97).
        if (IsScanning)
            CancelScan();

        if (!_orchestrator.IsRunning && !_aiOrchestrator.IsRunning)
            return;
        _orchestrator.Stop();
        _aiOrchestrator.Stop();
        OrchestratorRunning = false;
        StopProcessMonitor();
        Logs.Add("[Оркестратор] Остановлен.");
    }

    // ── Применяем состояние OrchestratorEnabled ──
    // Вызывается при изменении флага через чекбокс/кнопку и при старте/стопе Zapret.
    internal void ApplyOrchestratorEnabledState()
    {
        if (OrchestratorEnabled)
        {
            // "Вооружён": если Zapret уже работает — сразу запускаем сервисы.
            if (IsRunning)
                _ = StartOrchestratorServicesAsync();
            else
                Logs.Add("[Оркестратор] Режим авто: ожидаю запуск Zapret...");
        }
        else
        {
            // "Разоружён": останавливаем сервисы.
            // Если Zapret работает — перезапускаем его в ручном режиме.
            var wasRunning = IsRunning;
            StopOrchestratorServices();
            if (wasRunning)
            {
                Logs.Add("[Оркестратор] Переход в ручной режим — перезапуск Zapret...");
                _suppressOrchestratorStop = true;
                Stop();
                _suppressOrchestratorStop = false;
                Start();
            }
        }
    }

    // ── Кнопка "Запустить/Остановить оркестратор" на вкладке ──
    [RelayCommand]
    private void ToggleOrchestrator()
    {
        OrchestratorEnabled = !OrchestratorEnabled;
        SaveSettings();
        ApplyOrchestratorEnabledState();
    }

    // ── Вызывается из StartWinwsDirect/StartViaBatFallback после успешного старта ──
    private void TryStartOrchestratorIfEnabled()
    {
        if (OrchestratorEnabled)
            _ = StartOrchestratorServicesAsync();
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        // Счётчик поколений: только последний вызов сбрасывает IsScanning в finally
        var gen = ++_scanGeneration;

        // Отменяем предыдущую проверку, если была
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var checkCt = _scanCts.Token;

        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] Запуск ручной проверки...");
        IsScanning = true;
        OnPropertyChanged(nameof(CanCancelScan));
        ScanProgressText = "Ручная проверка...";
        // Синхронизируем список активных сайтов перед проверкой
        UpdateOrchestratorEnabledSites();
        try
        {
            if (AiEnabled)
            {
                // ═══ v1.7.1: «Проверить сейчас» проверяет только ВЫБРАННУЮ стратегию,
                // а не гонит полный скан и не удаляет эволюции (issue #89).
                // ProbeSelectedStrategyAsync не переподбирает/не эволюционирует, а только
                // проверяет текущую стратегию и пишет результат в генотип.
                var wasRunningBefore = IsTrackedProcessRunning();
                try
                {
                    await _aiOrchestrator.ProbeSelectedStrategyAsync(checkCt).ConfigureAwait(false);
                }
                finally
                {
                    // ProbeAsync (StopAfterProbe=false) оставляет winws запущенным, а внутри
                    // SwitchProfileAsync всегда стартует защиту. Восстанавливаем состояние в
                    // finally, чтобы оно применилось и при отмене/ошибке пробы — иначе ручная
                    // проверка незаметно запускала winws и ИИ-оркестратор (Codex P1, 12/13-й раунд).
                    if (!wasRunningBefore && IsTrackedProcessRunning())
                        Stop();
                }

                var d = Application.Current?.Dispatcher;
                if (d is not null && !d.HasShutdownStarted && !d.HasShutdownFinished)
                {
                    _ = d.BeginInvoke(new Action(() =>
                    {
                        RebuildAiStrategyRows();
                        RefreshAiDashboard();
                    }));
                }
            }
            else
                await _orchestrator.CheckNowAsync(checkCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (gen == _scanGeneration)
                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⏹ Проверка отменена.");
        }
        catch (Exception ex)
        {
            if (gen == _scanGeneration)
            {
                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка проверки: {ex.Message}");
                Logs.Add($"[Оркестратор] Ошибка проверки: {ex.Message}");
            }
        }
        finally
        {
            if (gen == _scanGeneration)
            {
                IsScanning = false;
                ScanProgressText = "";
                OnPropertyChanged(nameof(CanCancelScan));
            }
        }
    }

    [RelayCommand]
    private void ClearOrchestratorLogs()
    {
        OrchestratorLogs.Clear();
    }

    // ═══ v1.7.1: Отдельное действие очистки слабых эволюций (issue #89).
    // Раньше автоудаление происходило скрыто внутри «Проверить сейчас»; теперь это
    // явный шаг, подтверждаемый пользователем.
    [RelayCommand]
    private async Task CleanWeakEvolutions()
    {
        if (!AiEnabled)
        {
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⚠️ Очистка эволюций доступна только в режиме ИИ.");
            return;
        }

        // Сериализация очистки с проверками/сканированием (правка Codex P1, восьмой раунд):
        // иначе очистка может удалить генотип/BAT, пока TryProbeAndPersistGenomeAsync ещё работает,
        // и после завершения проверки тот воскресит запись в реестре удалённого файла.
        if (IsScanning)
        {
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⚠️ Дождитесь завершения текущей проверки/сканирования перед очисткой эволюций.");
            return;
        }

        if (!CustomDialog.Show(
            "Очистить слабые эволюции",
            $"Удалить эволюционированные стратегии с результатом ниже {AiAutoDeleteBelowScore}%?\n\n" +
            "Встроенные стратегии удалены не будут.",
            "Очистить",
            "Отмена",
            isDanger: true))
            return;

        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 🗑 Запуск очистки слабых эволюций (порог {AiAutoDeleteBelowScore}%)...");
        var activeBeforePurge = SelectedProfile;
        var wasRunning = IsTrackedProcessRunning();
        // Захватываем генотип, соответствующий активному профилю, ДО очистки —
        // по нему потом определяем, была ли активная стратегия именно удалена
        // (а не просто отсутствовала в реестре, как кастомная BAT без генотипа).
        var activeGenomeBefore = activeBeforePurge is null
            ? null
            : _aiRegistry.GetGenomes().FirstOrDefault(g =>
                string.Equals(g.BatFileName, activeBeforePurge.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.DisplayName, activeBeforePurge.DisplayName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.SourceBatPath, activeBeforePurge.FullPath, StringComparison.OrdinalIgnoreCase));
        try
        {
            var deleted = await _aiOrchestrator.PurgeWeakEvolutionsAsync().ConfigureAwait(true);
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 🗑 Удалено слабых эволюций: {deleted}");
            Logs.Add($"[ИИ] Очистка слабых эволюций: удалено {deleted}.");

            // Активная стратегия удалена очисткой ⇔ ей соответствовал генотип ДО очистки,
            // и теперь этого генотипа больше нет в реестре (правка по Codex P2, седьмой раунд).
            var activeDeleted = activeGenomeBefore is not null &&
                _aiRegistry.GetById(activeGenomeBefore.Id) is null;

            // Останавливаем защиту ДО перезагрузки профилей, если активную стратегию удалили.
            // Иначе переключение на новый профиль перезапустит защиту, а следующий Stop() убил бы
            // её и ИИ-оркестратор — и защита осталась бы выключенной (правка по Codex P1, шестой раунд).
            if (activeDeleted && wasRunning && IsRunning)
                Stop();

            // Подавляем смену профиля при перезагрузке ВСЕГДА: даже если активная эволюция не
            // удалялась, LoadProfiles() пересоздаёт объекты профилей и может поднять ложное
            // предупреждение/перезапуск защиты (правка по Codex P2, десятый раунд).
            _suppressProfileWarning = true;
            try
            {
                RebuildAiStrategyRows();
                RefreshAiDashboard();
                LoadProfiles();

                if (activeDeleted)
                {
                    SelectedProfile = Profiles.FirstOrDefault();
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ↩ Профиль «{activeBeforePurge!.DisplayName}» удалён — переключено на «{SelectedProfile?.DisplayName ?? "—"}».");

                    // Возвращаем защиту в исходное состояние запущенности на новом профиле.
                    if (wasRunning && SelectedProfile is not null && !IsTrackedProcessRunning())
                        Start();
                }
            }
            finally
            {
                _suppressProfileWarning = false;
            }
        }
        catch (Exception ex)
        {
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка очистки эволюций: {ex.Message}");
            Logs.Add($"[ИИ] Ошибка очистки эволюций: {ex.Message}");
        }
    }

    private bool IsTrackedProcessRunning()
    {
        try
        {
            return _runningProcess is not null && !_runningProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // ── Мониторинг процессов для автопереключения пресетов ──
    private CancellationTokenSource? _processMonitorCts;
    private ProfileItem? _profileBeforeTrigger; // профиль, активный до срабатывания триггера (для возврата)

    private void StartProcessMonitor()
    {
        if (_processMonitorCts is not null) return;
        _processMonitorCts = new CancellationTokenSource();
        var ct = _processMonitorCts.Token;
        Task.Run(async () => await ProcessMonitorLoopAsync(ct).ConfigureAwait(false), ct);
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 👁 Мониторинг процессов запущен.");
    }

    private void StopProcessMonitor()
    {
        _processMonitorCts?.Cancel();
        _processMonitorCts = null;
        _activeTriggeredPreset = null;
        _profileBeforeTrigger = null; // Очищаем запомненный профиль
    }

    private string? _activeTriggeredPreset; // Id активного тригерного пресета

    private async Task ProcessMonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) break;
            await dispatcher.InvokeAsync(() =>
            {
                try { CheckProcessTriggers(); }
                catch (Exception ex) { AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ⚠ Ошибка мониторинга: {ex.Message}"); }
            });
        }
    }

    private void CheckProcessTriggers()
    {
        // Ищем первый пресет с триггером, чей процесс сейчас запущен
        ConfigPreset? matched = null;
        var triggeredPresets = Presets.Where(p => !string.IsNullOrWhiteSpace(p.TriggerProcess)).ToList();

        if (triggeredPresets.Count == 0)
        {
            // Нет пресетов с триггером — нечего мониторить
            return;
        }

        foreach (var p in triggeredPresets)
        {
            var raw = p.TriggerProcess.Trim();
            var exeName = System.IO.Path.GetFileNameWithoutExtension(raw);
            var procs = System.Diagnostics.Process.GetProcessesByName(exeName);
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 🔍 Ищу «{exeName}» (из «{raw}») → найдено: {procs.Length}");

            if (procs.Length > 0)
            {
                matched = p;
                break;
            }
        }

        var matchedId = matched?.Id.ToString();

        if (matchedId != _activeTriggeredPreset)
        {
            if (matched is not null)
            {
                // Процесс появился → запоминаем текущий профиль и применяем пресет
                _activeTriggeredPreset = matchedId;

                // ═══ v1.6.0: Запоминаем профиль перед триггером ═══
                // Если не задан дефолтный профиль, используем текущий
                if (string.IsNullOrEmpty(DefaultProfileFileName))
                {
                    _profileBeforeTrigger = SelectedProfile;
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 📌 Запомнен профиль до триггера: {_profileBeforeTrigger?.DisplayName ?? "—"}");
                }
                else
                {
                    // Дефолтный профиль задан, найдём его
                    _profileBeforeTrigger = Profiles.FirstOrDefault(p => p.FileName == DefaultProfileFileName);
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 📌 Используется дефолтный профиль: {_profileBeforeTrigger?.DisplayName ?? DefaultProfileFileName}");
                }
                // ═════════════════════════════════════════════════

                AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 🎮 Обнаружен процесс «{matched.TriggerProcess}» → применяю пресет «{matched.Name}»");
                _ = ApplyPreset(matched);
            }
            else if (_activeTriggeredPreset is not null)
            {
                // ═══ v1.6.0: Процесс исчез → возвращаем запомненный профиль ═══
                _activeTriggeredPreset = null;

                if (_profileBeforeTrigger is not null)
                {
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ↩ Процесс завершён → возврат на профиль «{_profileBeforeTrigger.DisplayName}»");
                    // Применяем запомненный профиль
                    if (SelectedProfile != _profileBeforeTrigger)
                    {
                        _suppressProfileWarning = true;
                        SelectedProfile = _profileBeforeTrigger;
                        _suppressProfileWarning = false;

                        // Перезапускаем защиту если она была запущена
                        if (IsRunning)
                        {
                            Stop();
                            _ = Task.Delay(1000).ContinueWith(_ =>
                            {
                                Application.Current?.Dispatcher.Invoke(Start);
                            });
                        }
                    }
                    _profileBeforeTrigger = null;
                }
                else
                {
                    // Если профиль не был запомнен, пытаемся вернуться к первому пресету без триггера
                    var fallback = Presets.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.TriggerProcess));
                    if (fallback is not null)
                    {
                        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ↩ Процесс завершён → возврат к пресету «{fallback.Name}»");
                        _ = ApplyPreset(fallback);
                    }
                    else
                    {
                        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ↩ Процесс завершён (профиль/пресет возврата не задан)");
                    }
                }
                // ═══════════════════════════════════════════════════════════════
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  v1.6.0: Вкладка HOSTS — просмотр и редактирование системного hosts
    // ═══════════════════════════════════════════════════════════════

    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";

    [ObservableProperty] private string hostsFileContent = "";
    [ObservableProperty] private string hostsStatusText = "";
    [ObservableProperty] private bool hostsHasChanges;

    /// <summary>Переключает режим на «Hosts» и загружает файл.</summary>
    [RelayCommand]
    private void SetHostsMode()
    {
        SelectedTabMode = "Hosts";
        NewSiteInput = "";
        LoadHostsFile();
    }

    /// <summary>Читает системный файл hosts.</summary>
    private void LoadHostsFile()
    {
        try
        {
            if (!File.Exists(HostsFilePath))
            {
                HostsFileContent = $"# Файл не найден: {HostsFilePath}";
                HostsStatusText = "❌ Файл отсутствует";
                HostsHasChanges = false;
                return;
            }

            HostsFileContent = File.ReadAllText(HostsFilePath, new UTF8Encoding(false));
            var lines = HostsFileContent.Split('\n').Length;
            HostsStatusText = $"📄 {HostsFilePath} ({lines} стр.)";
            HostsHasChanges = false;
        }
        catch (UnauthorizedAccessException)
        {
            HostsFileContent = "# Нет прав на чтение файла.\n# Запустите приложение от имени администратора.";
            HostsStatusText = "🔒 Нет прав на чтение";
            HostsHasChanges = false;
        }
        catch (Exception ex)
        {
            HostsFileContent = $"# Ошибка чтения: {ex.Message}";
            HostsStatusText = "❌ Ошибка чтения";
            HostsHasChanges = false;
        }
    }

    /// <summary>Сохраняет изменения в hosts с резервной копией.</summary>
    [RelayCommand]
    private void SaveHosts()
    {
        try
        {
            if (!CustomDialog.Show(
                    "💾 Сохранить hosts?",
                    "Изменения будут записаны в системный файл hosts. Предыдущая версия будет сохранена в .bak.",
                    "Сохранить", "Отмена", isDanger: false))
                return;

            // Резервная копия
            var bakPath = HostsFilePath + ".bak";
            if (File.Exists(HostsFilePath))
                File.Copy(HostsFilePath, bakPath, overwrite: true);

            File.WriteAllText(HostsFilePath, HostsFileContent, new UTF8Encoding(false));

            HostsHasChanges = false;
            var lines = HostsFileContent.Split('\n').Length;
            HostsStatusText = $"✅ Сохранено ({lines} стр.) · резервная копия: hosts.bak";
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] 💾 hosts сохранён ({lines} стр.)");
        }
        catch (UnauthorizedAccessException)
        {
            CustomDialog.Show(
                "🔒 Нет прав на запись",
                "Для редактирования системного файла hosts требуются права администратора.\n\nПерезапустите FluxRoute от имени администратора.",
                "OK", isDanger: true);
            HostsStatusText = "🔒 Нет прав — перезапустите от админа";
        }
        catch (Exception ex)
        {
            CustomDialog.Show(
                "❌ Ошибка сохранения",
                $"Не удалось записать файл hosts:\n{ex.Message}",
                "OK", isDanger: true);
            HostsStatusText = $"❌ Ошибка: {ex.Message}";
        }
    }

    /// <summary>Отменяет изменения — перечитывает файл.</summary>
    [RelayCommand]
    private void RevertHosts()
    {
        LoadHostsFile();
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ↩ hosts: изменения отменены");
    }

    partial void OnHostsFileContentChanged(string value)
    {
        // Флаг изменений поднимается только если мы в режиме Hosts
        if (SelectedTabMode == "Hosts")
            HostsHasChanges = true;
    }
}