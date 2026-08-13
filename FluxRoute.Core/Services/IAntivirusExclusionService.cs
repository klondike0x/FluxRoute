// ═══ v1.7.0: НОВОЕ — сервис исключений Защитника Windows ═══
namespace FluxRoute.Core.Services;

public class AntivirusExclusionResult
{
    public string Path { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RequiresElevation { get; init; }
}

public interface IAntivirusExclusionService
{
    Task<AntivirusExclusionResult> AddExclusionAsync(string folderPath, CancellationToken ct = default);
    Task<bool> IsExcludedAsync(string folderPath, CancellationToken ct = default);
}
