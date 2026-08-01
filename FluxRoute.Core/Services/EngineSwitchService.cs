// ═══ v1.7.0: EngineSwitchService ═══
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public enum EngineType { Zapret, Zapret2 }
public class EngineSwitchResult { public bool Success; public EngineType ActiveEngine; public string? Error; public bool Tested; }

public class EngineSwitchService : IEngineSwitchService
{
    private readonly ILogger<EngineSwitchService> _log;
    public EngineSwitchService(ILogger<EngineSwitchService> log) => _log = log;
    public Task<bool> CanSwitchToAsync(string type, CancellationToken ct = default)
        => Task.FromResult(true);
    public async Task<bool> TestStrategyAsync(string path, IReadOnlyList<string> sites, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) { _log.LogWarning("Путь не указан"); return false; }
        if (sites is null || sites.Count == 0) return true;
        int ok = 0;
        foreach (var s in sites) { ct.ThrowIfCancellationRequested(); try { using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(5) }; if ((await h.GetAsync($"https://{s}", ct)).IsSuccessStatusCode) ok++; } catch { } }
        return (double)ok / sites.Count >= 0.5;
    }
}
