// ═══ v1.7.0: MtProtoProxyService (stub) ═══
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public class MtProtoProxyService : IMtProtoProxyService
{
    private readonly ILogger<MtProtoProxyService> _log;
    public MtProtoState State { get; private set; } = MtProtoState.Stopped;
    public MtProtoConfig Config { get; } = new();

    public MtProtoProxyService(ILogger<MtProtoProxyService> log) { _log = log; GenerateSecret(); }

    public void GenerateSecret() { Config.Secret = Convert.ToHexString(Guid.NewGuid().ToByteArray())[..32]; _log.LogInformation("Сгенерирован секрет MTProto"); }

    public Task StartAsync(CancellationToken ct = default) { State = MtProtoState.Running; _log.LogInformation("MTProto прокси запущен на порту {Port}", Config.Port); return Task.CompletedTask; }

    public Task StopAsync(CancellationToken ct = default) { State = MtProtoState.Stopped; _log.LogInformation("MTProto прокси остановлен"); return Task.CompletedTask; }
}
