using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.AI.Models;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
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

    // ═══ v1.6.0: Статус подбора стратегии ═══
    [ObservableProperty] private bool isFindingStrategy;
    [ObservableProperty] private string findStrategyResultText = "";

    /// <summary>
    /// v1.6.0: Подбирает лучшую стратегию для заданного домена/IP.
    /// Проверяет активные стратегии (топ-10 по Wilson score),
    /// не делает полного сканирования. Если ни одна не подходит
    /// (Score > 70%), пытается создать новую через эволюцию.
    /// </summary>
    [RelayCommand]
    private async Task FindBestStrategyForTarget()
    {
        var target = FindBestStrategyTarget?.Trim();
        if (string.IsNullOrEmpty(target))
        {
            FindStrategyResultText = "Введите домен или IP.";
            return;
        }

        if (IsFindingStrategy) return;
        IsFindingStrategy = true;
        FindStrategyResultText = $"🔍 Подбор стратегии для «{target}»...";
        Logs.Add($"🧠 Подбор стратегии для: {target}");
        AddToRecentLogs($"🧠 Подбор стратегии для: {target}");

        try
        {
            // Нормализация: убираем протоколы
            target = target.Replace("http://", "").Replace("https://", "").TrimEnd('/');

            var targetEntry = new TargetEntry
            {
                Key = target,
                Kind = TargetKind.Http,
                Value = target.Contains("://") ? target : $"https://{target}"
            };

            // Берём активные стратегии, сортируем по Wilson score (топ-10)
            var genomes = _aiRegistry.GetActiveGenomes()
                .Select(g =>
                {
                    var outcomes = _aiHistoryStore.LoadAll().Where(o => o.GenomeId == g.Id).ToList();
                    var succ = outcomes.Count(o => o.Score >= 50);
                    var trials = outcomes.Count;
                    var w = FluxRoute.AI.Stats.WilsonScore.LowerBound(succ, trials);
                    return (genome: g, wilson: w);
                })
                .OrderByDescending(x => x.wilson)
                .Take(10)
                .Select(x => x.genome)
                .ToList();

            if (genomes.Count == 0)
            {
                FindStrategyResultText = "Нет активных стратегий для проверки.";
                return;
            }

            var probeService = new ProfileProbeService(_connectivity, async (p) =>
            {
                await SwitchProfileAsync(p);
                if (p is not null)
                    await Task.Delay(3000); // Ждём стабилизации winws
            });

            var previousProfile = SelectedProfile;
            var wasRunning = IsRunning;
            ProfileProbeResult? bestResult = null;
            StrategyGenome? bestGenome = null;
            int bestScore = 0;

            try
            {
                foreach (var g in genomes)
                {
                    var profile = Profiles.FirstOrDefault(p =>
                        string.Equals(p.FileName, g.BatFileName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.DisplayName, g.DisplayName, StringComparison.OrdinalIgnoreCase));

                    if (profile is null) continue;

                    FindStrategyResultText = $"🔍 Проверяю «{g.DisplayName}»...";
                    Logs.Add($"[🧠] Тестирую стратегию «{g.DisplayName}» для {target}");

                    var result = await probeService.ProbeAsync(profile, new[] { targetEntry },
                        new ProfileProbeOptions
                        {
                            StartupWait = TimeSpan.FromSeconds(4),
                            StableWait = TimeSpan.FromMilliseconds(1500),
                            ProcessWaitTimeout = TimeSpan.FromSeconds(10),
                            StopAfterProbe = false,
                            RequireWinwsProcess = true
                        });

                    Logs.Add($"[🧠] «{g.DisplayName}» → Score: {result.Score}% ({result.Summary})");

                    if (result.Score > bestScore)
                    {
                        bestScore = result.Score;
                        bestResult = result;
                        bestGenome = g;
                    }

                    if (result.Score > 70)
                        break; // Нашли хорошую — останавливаемся
                }

                if (bestScore > 70 && bestGenome is not null)
                {
                    // Переключаемся на лучшую стратегию
                    var bestProfile = Profiles.FirstOrDefault(p =>
                        string.Equals(p.DisplayName, bestGenome.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (bestProfile is not null)
                    {
                        await SwitchProfileAsync(bestProfile);
                    }

                    var msg = $"✅ Найдена стратегия для «{target}»: {bestGenome.DisplayName} ({bestScore}%)";
                    FindStrategyResultText = msg;
                    Logs.Add(msg);
                    AddToRecentLogs($"🧠 Найдена стратегия: {bestGenome.DisplayName} ({bestScore}%)");

                    _trayIcon?.ShowBalloon("FluxRoute", $"🧠 Найдена стратегия для {target}: {bestGenome.DisplayName} ({bestScore}%)");
                    _trayIcon?.UpdateTooltip($"FluxRoute — {bestGenome.DisplayName}");
                    return;
                }

                // Ни одна не подошла — пытаемся эволюционировать
                FindStrategyResultText = $"🧬 Ни одной подходящей, запускаю эволюцию...";
                Logs.Add($"[🧠] Ни одна стратегия не дала Score > 70%, запускаю эволюцию.");

                var fp = _aiFingerprints.Capture();
                var child = await Task.Run(() => _evolver.Evolve(fp));
                if (child is not null)
                {
                    _aiOrchestrator.SyncRegistryFromEngine();
                    await RefreshProfilesInternalAsync();

                    // Материализуем .bat файл
                    var evolvedDir = Path.Combine(EngineDir, "ai-evolved");
                    Directory.CreateDirectory(evolvedDir);
                    var batPath = Path.Combine(evolvedDir, $"{child.DisplayName}.bat");
                    if (!File.Exists(batPath))
                    {
                        var materializer = new FluxRoute.AI.Services.BatMaterializer();
                        materializer.WriteBat(child, EngineDir);
                    }

                    LoadProfiles();
                    RebuildAiStrategyRows();

                    var msg = $"🧬 ИИ создал стратегию «{child.DisplayName}» для {target}";
                    FindStrategyResultText = msg;
                    Logs.Add($"🧠 {msg}");
                    AddToRecentLogs($"🧠 {msg}");

                    _trayIcon?.ShowBalloon("FluxRoute", $"🧠 ИИ создал стратегию для {target}: {child.DisplayName}");
                    _trayIcon?.UpdateTooltip($"FluxRoute — {child.DisplayName}");
                }
                else
                {
                    FindStrategyResultText = $"❌ Не удалось подобрать стратегию для «{target}».";
                    Logs.Add($"[🧠] Эволюция не создала новую стратегию.");
                }
            }
            finally
            {
                // Возвращаем предыдущий профиль, если не нашли замену и был запущен winws
                if (bestScore <= 70 && previousProfile is not null && wasRunning)
                {
                    await SwitchProfileAsync(previousProfile);
                }
            }
        }
        catch (Exception ex)
        {
            FindStrategyResultText = $"❌ Ошибка: {ex.Message}";
            Logs.Add($"[🧠] Ошибка подбора: {ex.Message}");
        }
        finally
        {
            IsFindingStrategy = false;
        }
    }

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