// ═══ v1.7.0: StrategyEditorService ═══
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public class StrategyEditorService : IStrategyEditorService
{
    private readonly ILogger<StrategyEditorService> _log;
    public StrategyEditorService(ILogger<StrategyEditorService> log) => _log = log;

    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default) { if (!File.Exists(path)) return ""; return await File.ReadAllTextAsync(path, ct); }

    public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
    {
        CreateBackup(path);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, ct);
        File.Move(tmp, path, true);
        _log.LogInformation("Файл сохранён: {Path}", path);
    }

    public IReadOnlyList<string> FindFiles(string dir, string pattern) { if (!Directory.Exists(dir)) return Array.Empty<string>(); return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories); }

    public void CreateBackup(string path) { if (File.Exists(path)) { var bak = path + $".bak.{DateTime.Now:yyyyMMddHHmmss}"; File.Copy(path, bak, true); _log.LogDebug("Бекап создан: {Bak}", bak); } }
}
