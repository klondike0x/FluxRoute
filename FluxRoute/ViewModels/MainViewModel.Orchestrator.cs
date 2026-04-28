using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.ViewModels;

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
            }
            catch (Exception ex)
            {
                Logs.Add($"[Оркестратор] Ошибка UI-обновления: {ex.Message}");
            }
        }));
    }

    private void UpdateOrchestratorNextCheck()
    {
        if (_orchestrator.NextCheckAt is { } next)
        {
            var remaining = next - DateTimeOffset.Now;
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
            Stop();

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
    }

    [RelayCommand]
    private async Task ScanProfiles()
    {
        RebuildProfileScores();
        IsScanning = true;
        ScanProgressText = "Сканирование...";
        UpdateOrchestratorEnabledSites();

        try
        {
            await _orchestrator.ScanAllProfilesAsync();
            SortProfileScores();
            ScanProgressText = "Сканирование завершено";
            SaveSettings();
        }
        catch (Exception ex)
        {
            ScanProgressText = "Ошибка сканирования";
            AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] ❌ Ошибка сканирования: {ex.Message}");
            Logs.Add($"[Оркестратор] Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ToggleOrchestrator()
    {
        if (_orchestrator.IsRunning)
        {
            _orchestrator.Stop();
            OrchestratorRunning = false;
        }
        else
        {
            if (int.TryParse(OrchestratorInterval, out var mins) && mins >= 1)
                _orchestrator.CheckInterval = TimeSpan.FromMinutes(mins);

            UpdateOrchestratorEnabledSites();

            if (ProfileScores.Count == 0 || ProfileScores.All(s => s.Score == 0))
                RebuildProfileScores();

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
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        AddOrchestratorLog($"[{DateTime.Now:HH:mm:ss}] Запуск ручной проверки...");

        try
        {
            await _orchestrator.CheckNowAsync();
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
