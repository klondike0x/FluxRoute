using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.AI.Models;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
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

    [ObservableProperty] private bool useHybridMode;
    partial void OnUseHybridModeChanged(bool value) => SaveSettings();

    [ObservableProperty] private int engineMode;
    partial void OnEngineModeChanged(int value)
    {
        SaveSettings();
        var runMode = value switch
        {
            (int)DpiEngineMode.ByeDpi => DpiRunMode.Standalone,
            (int)DpiEngineMode.Warp => DpiRunMode.Warp,
            (int)DpiEngineMode.Hybrid => DpiRunMode.Hybrid,
            (int)DpiEngineMode.WarpZapret => DpiRunMode.WarpZapret,
            (int)DpiEngineMode.WarpByeDpi => DpiRunMode.WarpByeDpi,
            _ => DpiRunMode.Standalone
        };
        _engineManager.SetRunMode(runMode);
    }

    [ObservableProperty] private int byeDpiSocksPort = 1080;
    partial void OnByeDpiSocksPortChanged(int value) => SaveSettings();

    [ObservableProperty] private string? byeDpiSplitPos;
    partial void OnByeDpiSplitPosChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiDisorderPos = "1";
    partial void OnByeDpiDisorderPosChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiFakePos;
    partial void OnByeDpiFakePosChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiOobPos;
    partial void OnByeDpiOobPosChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiTlsrecPos;
    partial void OnByeDpiTlsrecPosChanged(string? value) => SaveSettings();

    [ObservableProperty] private int? byeDpiFakeTtl;
    partial void OnByeDpiFakeTtlChanged(int? value) => SaveSettings();

    [ObservableProperty] private bool byeDpiAutoTtl;
    partial void OnByeDpiAutoTtlChanged(bool value) => SaveSettings();

    [ObservableProperty] private string? byeDpiAutoMode = "torst";
    partial void OnByeDpiAutoModeChanged(string? value) => SaveSettings();

    [ObservableProperty] private int? byeDpiTimeout;
    partial void OnByeDpiTimeoutChanged(int? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiHosts;
    partial void OnByeDpiHostsChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiHostlist;
    partial void OnByeDpiHostlistChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiFakeTlsMod;
    partial void OnByeDpiFakeTlsModChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiFakeSni;
    partial void OnByeDpiFakeSniChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiFakeData;
    partial void OnByeDpiFakeDataChanged(string? value) => SaveSettings();

    [ObservableProperty] private string? byeDpiModHttp;
    partial void OnByeDpiModHttpChanged(string? value) => SaveSettings();

    [ObservableProperty] private int? byeDpiTlsminor;
    partial void OnByeDpiTlsminorChanged(int? value) => SaveSettings();

    [ObservableProperty] private bool byeDpiMd5sig;
    partial void OnByeDpiMd5sigChanged(bool value) => SaveSettings();

    [ObservableProperty] private string aiNetworkLabel = "—";
    [ObservableProperty] private string aiGenerationText = "—";
    [ObservableProperty] private string aiProbeCountText = "—";

    private AiSettings BuildAiSettingsSnapshot()
    {
        return new AiSettings
        {
            Enabled = AiEnabled,
            ExplorationRatePermil = AiExplorationPermil,
            AutoDeleteBelowScore = AiAutoDeleteBelowScore,
            EngineMode = EngineMode,
            UseHybridMode = EngineMode == 2,
            ByeDpiSocksPort = ByeDpiSocksPort,
            ByeDpiDefaults = BuildByeDpiDefaultsSnapshot(),
        };
    }

    private ByeDpiProfileSettings BuildByeDpiDefaultsSnapshot() => new()
    {
        SocksPort = ByeDpiSocksPort,
        SplitPos = ByeDpiSplitPos,
        DisorderPos = ByeDpiDisorderPos,
        FakePos = ByeDpiFakePos,
        OobPos = ByeDpiOobPos,
        TlsrecPos = ByeDpiTlsrecPos,
        FakeTtl = ByeDpiFakeTtl,
        AutoTtl = ByeDpiAutoTtl,
        Auto = ByeDpiAutoMode,
        Timeout = ByeDpiTimeout,
        Hosts = ByeDpiHosts,
        Hostlist = ByeDpiHostlist,
        FakeTlsMod = ByeDpiFakeTlsMod,
        FakeSni = ByeDpiFakeSni,
        FakeData = ByeDpiFakeData,
        ModHttp = ByeDpiModHttp,
        Tlsminor = ByeDpiTlsminor,
        Md5sig = ByeDpiMd5sig,
    };

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

        _aiOrchestrator.SyncRegistryFromEngine();
        LoadProfiles();
        RefreshAiDashboard();
        RebuildAiStrategyRows();
        Logs.Add("[ИИ] Модель сброшена.");
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