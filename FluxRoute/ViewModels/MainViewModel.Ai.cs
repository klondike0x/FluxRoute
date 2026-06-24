using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.AI.Models;
using FluxRoute.Core.Models;
using FluxRoute.Views;
using Application = System.Windows.Application;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool aiEnabled;
    partial void OnAiEnabledChanged(bool value)
    {
        SaveSettings();
        if (value)
            RebuildAiStrategyRows();
    }

    [ObservableProperty] private int aiExplorationPermil = 100;
    partial void OnAiExplorationPermilChanged(int value) => SaveSettings();

    [ObservableProperty] private int aiAutoDeleteBelowScore = 60;
    partial void OnAiAutoDeleteBelowScoreChanged(int value) => SaveSettings();

    [ObservableProperty] private string aiNetworkLabel = "—";
    [ObservableProperty] private string aiGenerationText = "—";
    [ObservableProperty] private string aiProbeCountText = "—";

    private AiSettings BuildAiSettingsSnapshot()
    {
        var s = _settingsService.Load().Ai;
        s.Enabled = AiEnabled;
        s.ExplorationRatePermil = AiExplorationPermil;
        s.AutoDeleteBelowScore = AiAutoDeleteBelowScore;
        return s;
    }

    private Task RefreshProfilesInternalAsync()
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.HasShutdownStarted || d.HasShutdownFinished)
            return Task.CompletedTask;

        return d.InvokeAsync(LoadProfiles).Task;
    }

    public void RefreshAiDashboard()
    {
        try
        {
            var fp = _aiFingerprints.Capture();
            AiNetworkLabel = fp.Label;
            AiGenerationText = _aiRegistry.GenerationCounter.ToString();
            AiProbeCountText = _aiHistoryStore.LoadAll().Count.ToString();
        }
        catch
        {
        }
    }

    [RelayCommand]
    private async Task RunAiEvolutionAsync()
    {
        _aiOrchestrator.SyncRegistryFromEngine();
        await _aiOrchestrator.EvolveNowAsync().ConfigureAwait(true);
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess())
        {
            await d.InvokeAsync(() =>
            {
                LoadProfiles();
                RefreshAiDashboard();
                RebuildAiStrategyRows();
            }).Task.ConfigureAwait(true);
        }
        else
        {
            LoadProfiles();
            RefreshAiDashboard();
            RebuildAiStrategyRows();
        }

        if (AiStrategyRows.Count == 0)
            Logs.Add("[ИИ] Список стратегий пуст. Обновите engine или запустите оркестратор.");
    }

    [RelayCommand]
    private void ResetAiModel()
    {
        _aiRegistry.ResetAll();
        var hist = Path.Combine(Path.GetDirectoryName(_settingsService.SettingsPath)!, "fluxroute-ai-history.jsonl");
        try
        {
            if (File.Exists(hist))
                File.Delete(hist);
        }
        catch
        {
        }

        var evolvedDir = Path.Combine(EngineDir, "ai-evolved");
        if (Directory.Exists(evolvedDir))
        {
            foreach (var bat in Directory.EnumerateFiles(evolvedDir, "*.bat"))
            {
                try { File.Delete(bat); } catch { }
            }
        }

        _aiOrchestrator.SyncRegistryFromEngine();
        LoadProfiles();
        RefreshAiDashboard();
        RebuildAiStrategyRows();
        Logs.Add("[ИИ] Модель сброшена, ai-evolved/ очищен.");
    }

    [RelayCommand]
    private void ClearAiEvolved()
    {
        if (!CustomDialog.Show(
            "Очистить ai-evolved",
            "Удалить все эволюционированные стратегии из engine/ai-evolved/?\nBAT-файлы и записи в реестре ИИ будут безвозвратно удалены.",
            "Очистить",
            "Отмена",
            isDanger: true))
            return;

        var evolvedDir = Path.Combine(EngineDir, "ai-evolved");
        if (Directory.Exists(evolvedDir))
        {
            foreach (var bat in Directory.EnumerateFiles(evolvedDir, "*.bat"))
            {
                try { File.Delete(bat); } catch { }
            }
        }

        var evolved = _aiRegistry.GetGenomes().Where(g => g.Origin == StrategyOrigin.Evolved).ToList();
        foreach (var g in evolved)
            _aiRegistry.Remove(g.Id);
        _aiRegistry.Save();

        _aiOrchestrator.SyncRegistryFromEngine();
        LoadProfiles();
        RefreshAiDashboard();
        RebuildAiStrategyRows();
        Logs.Add("[ИИ] ai-evolved очищен.");
    }

    [RelayCommand]
    private void OpenAiEvolvedFolder()
    {
        try
        {
            var p = Path.Combine(EngineDir, "ai-evolved");
            Directory.CreateDirectory(p);
            Process.Start(new ProcessStartInfo("explorer.exe", p) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logs.Add($"[ИИ] {ex.Message}");
        }
    }
}