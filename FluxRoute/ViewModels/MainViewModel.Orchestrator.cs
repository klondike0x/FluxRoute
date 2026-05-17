using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Application = System.Windows.Application;
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

public partial class MainViewModel
{
    private const int MaxLogEntries = 50;

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

        // Важно: не используем Dispatcher.Invoke(). Во время сканирования профилей события идут
        // из фоновых задач и из UI-потока вперемешку; синхронный Invoke может создать re-entrancy.
        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                AddOrchestratorLog(e.Message);
                OrchestratorStatus = e.Message;

                if (e.Message.Contains("Сканирование завершено", StringComparison.OrdinalIgnoreCase))
                {
                    SortProfileScores();
                    SaveSettings();
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
                Start();
            }
        }

        if (dispatcher.CheckAccess())
            SwitchOnUi();
        else
            await dispatcher.InvokeAsync(SwitchOnUi).Task.ConfigureAwait(false);
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

    private void UpdateOrchestratorEnabledSites()
    {
        var sites = new HashSet<string>();
        if (SiteYouTube) sites.Add("YouTube");
        if (SiteDiscord) sites.Add("Discord");
        if (SiteGoogle) sites.Add("Google");
        if (SiteTwitch) sites.Add("Twitch");
        if (SiteInstagram) sites.Add("Instagram");
        if (SiteTelegram) sites.Add("Telegram");
        _orchestrator.EnabledSites = sites;
        _aiOrchestrator.EnabledSites = sites;
    }

    [RelayCommand]
    private async Task ScanProfiles()
    {
        _orchestrator.ClearRankedProfiles();

        RebuildProfileScores();
        IsScanning = true;
        ScanProgressText = "Сканирование...";
        UpdateOrchestratorEnabledSites();

        var wasRunning = IsTrackedProcessRunning();
        try
        {
            _suppressOrchestratorStop = true;
            await _orchestrator.ScanAllProfilesAsync();
            SortProfileScores();
            ScanProgressText = "Сканирование завершено";
            SaveSettings();

            var top = ProfileScores.FirstOrDefault(s => s.Score > 0);
            if (top is not null)
            {
                var profile = Profiles.FirstOrDefault(p => p.FileName == top.FileName);
                if (profile is not null)
                {
                    AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ▶ Запуск лучшего профиля «{profile.DisplayName}» ({(int)System.Math.Round((double)top.Score * 100)}%).");
                    Logs.Add($"[Оркестратор] Лучший профиль после сканирования: «{profile.DisplayName}».");
                    await SwitchProfileAsync(profile).ConfigureAwait(false);
                }
            }
            else if (wasRunning && SelectedProfile is not null && !IsTrackedProcessRunning())
                await EnsureProtectionRunningAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ScanProgressText = "Ошибка сканирования";
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка сканирования: {ex.Message}");
            Logs.Add($"[Оркестратор] Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            _suppressOrchestratorStop = false;
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ToggleOrchestrator()
    {
        if (_orchestrator.IsRunning || _aiOrchestrator.IsRunning)
        {
            _orchestrator.Stop();
            _aiOrchestrator.Stop();
            OrchestratorRunning = false;
            return;
        }

        if (int.TryParse(OrchestratorInterval, out var mins) && mins >= 1)
        {
            var interval = TimeSpan.FromMinutes(mins);
            _orchestrator.CheckInterval = interval;
            _aiOrchestrator.CheckInterval = interval;
        }

        UpdateOrchestratorEnabledSites();

        if (ProfileScores.Count == 0 || ProfileScores.All(s => s.Score == 0))
            RebuildProfileScores();

        if (AiEnabled)
        {
            _aiOrchestrator.Start();
            OrchestratorRunning = true;
            return;
        }

        if (!IsTrackedProcessRunning())
        {
            if (SelectedProfile is not null)
            {
                Logs.Add("[Оркестратор] Автозапуск профиля...");
                Start();
            }
            else
            {
                Logs.Add("[Оркестратор] Профиль не выбран.");
                return;
            }
        }

        _orchestrator.Start();
        OrchestratorRunning = true;
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] Запуск ручной проверки...");

        try
        {
            if (AiEnabled)
            {
                await _aiOrchestrator.ProbeAllEnabledStrategiesAsync(CancellationToken.None).ConfigureAwait(false);
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
                await _orchestrator.CheckNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка проверки: {ex.Message}");
            Logs.Add($"[Оркестратор] Ошибка проверки: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearOrchestratorLogs()
    {
        OrchestratorLogs.Clear();
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
}
