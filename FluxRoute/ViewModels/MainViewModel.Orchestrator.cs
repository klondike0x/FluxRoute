using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
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
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            AddOrchestratorLog(e.Message);
            OrchestratorStatus = e.Message;

            if (e.Message.Contains("Сканирование завершено"))
            {
                var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
                ProfileScores.Clear();
                foreach (var s in sorted)
                    ProfileScores.Add(s);
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
        });
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
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Stop();

            if (profile is not null)
            {
                _suppressProfileWarning = true;
                SelectedProfile = profile;
                _suppressProfileWarning = false;
                Start();
            }
        });
    }

    private Task UpdateProfileScoreAsync(string fileName, int score)
    {
        if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
            return Task.CompletedTask;

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var entry = ProfileScores.FirstOrDefault(s => s.FileName == fileName);
            if (entry is null)
                return;

            if (score == -1)
                entry.SetPending();
            else
                entry.SetScore(score / 100.0);
        }).Task;
    }

    private void RebuildProfileScores()
    {
        ProfileScores.Clear();
        foreach (var p in Profiles)
            ProfileScores.Add(new ProfileScore { DisplayName = p.DisplayName, FileName = p.FileName });
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

            var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
            ProfileScores.Clear();
            foreach (var s in sorted)
                ProfileScores.Add(s);

            ScanProgressText = "Сканирование завершено";
            SaveSettings();
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

            if (_runningProcess is null || _runningProcess.HasExited)
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
        await _orchestrator.CheckNowAsync();
    }

    [RelayCommand]
    private void ClearOrchestratorLogs()
    {
        OrchestratorLogs.Clear();
    }
}
