// ═══ v1.7.0: AutoStrategyService — авто-подбор с fallback ═══
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public class AutoStrategyService
{
    private readonly ILogger<AutoStrategyService> _log;
    private readonly IEngineSwitchService _engine;
    public AutoStrategyService(ILogger<AutoStrategyService> log, IEngineSwitchService eng) { _log = log; _engine = eng; }

    public async Task<AutoStrategyResult> AutoPickAsync(IReadOnlyList<ProfileItem> profiles, IReadOnlyList<string> sites, int maxRetries = 3, CancellationToken ct = default)
    {
        if (profiles is null || profiles.Count == 0) return new() { Success = false, Error = "Нет стратегий" };
        var tested = new HashSet<string>(); var passed = new List<ProfileItem>();
        for (int a = 0; a < maxRetries; a++)
        {
            foreach (var p in profiles) { if (tested.Contains(p.FileName)) continue; tested.Add(p.FileName); ct.ThrowIfCancellationRequested(); if (await _engine.TestStrategyAsync(p.FullPath, sites, ct)) passed.Add(p); }
            if (passed.Count > 0) return new() { Success = true, Profile = passed[0].FileName, Tested = tested.Count, Passed = passed.Count };
            if (a < maxRetries - 1) { await Task.Delay(2000, ct); tested.Clear(); passed.Clear(); }
        }
        return new() { Success = false, Tested = tested.Count, Passed = 0, Error = $"0 из {tested.Count} работают" };
    }
}
