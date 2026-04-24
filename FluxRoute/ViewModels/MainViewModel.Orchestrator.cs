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
                foreach (var s in sorted) ProfileScores.Add(s);
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
            OrchestratorNextCheck = remaining > TimeSpan.Zero ? $"через {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}" : "сейчас...";
        }
        else OrchestratorNextCheck = "—";
    }

    private async Task SwitchProfileAsync(ProfileItem profile)
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
            if (entry is null) return;
            if (score == -1) entry.SetPending();
            else entry.SetScore(score / 100.0);
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

        await _orchestrator.ScanAllProfilesAsync();

        var sorted = ProfileScores.OrderByDescending(s => s.Score).ToList();
        ProfileScores.Clear();
        foreach (var s in sorted) ProfileScores.Add(s);

        IsScanning = false;
        ScanProgressText = "Сканирование завершено";
        SaveSettings();
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
            if (int.TryParse(OrchestratorInterval, out int mins) && mins >= 1)
                _orchestrator.CheckInterval = TimeSpan.FromMinutes(mins);

            UpdateOrchestratorEnabledSites();

            if (ProfileScores.Count == 0 || ProfileScores.All(s => s.Score == 0))
                RebuildProfileScores();

            if (_runningProcess is null || _runningProcess.HasExited)
            {
                if (SelectedProfile is not null) { Logs.Add("[Оркестратор] Автозапуск профиля..."); Start(); }
                else { Logs.Add("[Оркестратор] Профиль не выбран."); return; }
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

    [RelayCommand]
    private async Task RunAdvancedDiagnostics()
    {
        if (SelectedProfile is null)
        {
            AddOrchestratorLog("❌ Профиль не выбран.");
            return;
        }
        AddOrchestratorLog("🔍 Запуск расширенной диагностики...");
        var diag = await FluxRoute.Core.SystemDiagnostics.RunAsync();
        AddOrchestratorLog($"• WinDivert: {(diag.IsWinDivertRunning ? "✅ работает" : "❌ остановлен")}");
        AddOrchestratorLog($"• Порт 9888: {(diag.IsPortAvailable ? "✅ свободен" : "⚠ занят")}");
        AddOrchestratorLog($"• Интернет: {(diag.HasInternetAccess ? "✅ доступен" : "❌ отсутствует")}");
        if (!string.IsNullOrEmpty(diag.ErrorMessage))
        {
            AddOrchestratorLog($"⚠ Диагностика не пройдена: {diag.ErrorMessage}");
            return;
        }
        AddOrchestratorLog("⏳ Тестирование выбранного профиля...");
        var tester = new FluxRoute.Core.ProfileTester();
        var testUrl = "https://www.youtube.com"; // можно параметризовать позже
        var result = await tester.TestProfileAsync(SelectedProfile.DisplayName, testUrl);
        AddOrchestratorLog($"📊 Результаты для «{result.ProfileName}»:");
        AddOrchestratorLog($"   Latency: {result.LatencyMs} ms");
        AddOrchestratorLog($"   Stability: {result.StabilityRate * 100:F0}%");
        AddOrchestratorLog($"   Throughput: {result.SpeedMbps:F1} Mbps");
        AddOrchestratorLog($"   Score: {result.Score:F1}");
    }
    [RelayCommand]
    private async Task RunZapretDiagnostics()
    {
        AddOrchestratorLog("🔍 Запуск диагностики системы...");
        var diag = await FluxRoute.Core.SystemDiagnostics.RunAsync();
        AddOrchestratorLog($"• WinDivert (winws): {(diag.IsWinDivertRunning ? "✅ работает" : "❌ не запущен")}");
        AddOrchestratorLog($"• Порт 9888: {(diag.IsPortAvailable ? "✅ свободен" : "⚠ занят")}");
        AddOrchestratorLog($"• Интернет: {(diag.HasInternetAccess ? "✅ доступен" : "❌ отсутствует")}");
        if (!string.IsNullOrEmpty(diag.ErrorMessage))
        {
            AddOrchestratorLog($"❌ Критические проблемы: {diag.ErrorMessage}Запуск профиля может быть невозможен.");
            return;
        }
        AddOrchestratorLog("✅ Система готова к работе.");
    }
    private CancellationTokenSource? _advancedScanCts;

    [RelayCommand]
    private async Task ScanProfilesAdvanced()
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanProgressText = "Подготовка...";
        RebuildProfileScores();
        var service = new FluxRoute.Core.ProfileBenchmarkService();
        var profiles = Profiles.Select(p => (p.DisplayName, "https://www.youtube.com")).ToList();
        _advancedScanCts?.Cancel();
        _advancedScanCts = new CancellationTokenSource();

        service.ProgressChanged += (msg, pct, remaining) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ScanProgressText = $"{msg} ({pct:F0}%) — осталось ~{remaining} сек";
            });
        };

        try
        {
            var results = await service.BenchmarkProfilesAsync(profiles, _advancedScanCts.Token);
            ProfileScores.Clear();
            foreach (var r in results.OrderByDescending(r => r.Score))
            {
                var score = new ProfileScore
                {
                    DisplayName = r.ProfileName,
                    FileName = Profiles.FirstOrDefault(p => p.DisplayName == r.ProfileName)?.FileName ?? r.ProfileName
                };
                score.SetScore(r.Score / 100.0);
                ProfileScores.Add(score);
            }
            ScanProgressText = $"✅ Сканирование завершено — лучший: {ProfileScores.FirstOrDefault()?.DisplayName ?? "—"}";
        }
        catch (OperationCanceledException)
        {
            ScanProgressText = "⏹ Сканирование отменено";
        }
        finally
        {
            IsScanning = false;
            _advancedScanCts?.Dispose();
            _advancedScanCts = null;
        }
    }

    [RelayCommand]
    private void CancelAdvancedScan()
    {
        _advancedScanCts?.Cancel();
        AddOrchestratorLog("⏹ Расширенное сканирование отменено пользователем.");
    }}



