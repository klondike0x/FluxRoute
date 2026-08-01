// ═══ v1.7.0: Интерфейсы MTProto и StrategyEditor ═══
namespace FluxRoute.Core.Services;

public enum MtProtoState { Stopped, Starting, Running, Error }
public class MtProtoConfig { public int Port { get; set; } = 1080; public string Secret { get; set; } = ""; }

public interface IMtProtoProxyService
{
    MtProtoState State { get; }
    MtProtoConfig Config { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    void GenerateSecret();
}

public interface IStrategyEditorService
{
    Task<string> ReadFileAsync(string path, CancellationToken ct = default);
    Task WriteFileAsync(string path, string content, CancellationToken ct = default);
    IReadOnlyList<string> FindFiles(string engineDir, string pattern);
    void CreateBackup(string path);
}
