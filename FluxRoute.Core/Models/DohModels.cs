namespace FluxRoute.Core.Models;

/// <summary>Режим передачи DNS-запросов к выбранному провайдеру.</summary>
public enum DohEncryptionMode
{
    Unencrypted,
    EncryptedWithFallback,
    EncryptedOnly
}

/// <summary>Нормализованный глобальный режим Windows DoH.</summary>
public enum DohGlobalMode
{
    Disabled,
    Enabled,
    Automatic
}

/// <summary>Описание публичного DNS-over-HTTPS провайдера.</summary>
public sealed record DohProvider(
    string Id,
    string Name,
    Uri Template,
    IReadOnlyList<string> DnsAddresses,
    bool SupportsAiServices);

/// <summary>Результат серии тестовых DNS-запросов к провайдеру.</summary>
public sealed record DohProviderTestResult(
    DohProvider Provider,
    double? AverageLatencyMs,
    double SuccessRate,
    int AttemptCount,
    string? Error,
    double Score)
{
    public bool IsAvailable => SuccessRate > 0 && AverageLatencyMs is not null;
}

/// <summary>Состояние DNS отдельного семейства адресов.</summary>
public sealed record DnsFamilySnapshot(bool IsDhcp, IReadOnlyList<string> Addresses);

/// <summary>Состояние глобального сопоставления DNS-сервера с DoH.</summary>
public sealed record DohResolverMappingSnapshot(
    string Address,
    bool Exists,
    string? Template,
    bool AutoUpgrade,
    bool AllowFallbackToUdp);

/// <summary>Состояние DoH конкретного интерфейса.</summary>
public sealed record DohInterfaceMappingSnapshot(
    string Address,
    bool IsIpv6,
    bool Exists,
    string? Template,
    long? Flags);

/// <summary>Снимок системных слоёв DoH до начала транзакции.</summary>
public sealed record DohSystemStateSnapshot(
    DohGlobalMode GlobalDohMode,
    IReadOnlyList<DohResolverMappingSnapshot> ResolverMappings,
    IReadOnlyList<DohInterfaceMappingSnapshot> InterfaceMappings);

/// <summary>Персистентный журнал незавершённой системной операции DoH.</summary>
public sealed class DohOperationJournal
{
    public Guid OperationId { get; set; }
    public string InterfaceName { get; set; } = "";
    public string InterfaceId { get; set; } = "";
    public DnsFamilySnapshot Ipv4 { get; set; } = new(true, []);
    public DnsFamilySnapshot Ipv6 { get; set; } = new(true, []);
    public DohGlobalMode GlobalDohMode { get; set; } = DohGlobalMode.Disabled;
    public List<DohResolverMappingSnapshot> ResolverMappings { get; set; } = [];
    public List<DohInterfaceMappingSnapshot> InterfaceMappings { get; set; } = [];
}

/// <summary>Результат применения или отключения DoH в Windows.</summary>
public enum DohProviderSwitchOutcome
{
    AppliedNew,
    RestoredPrevious,
    RestoreFailed
}

/// <summary>Результат применения или отключения DoH в Windows.</summary>
public sealed record DohApplyResult(
    bool IsSuccess,
    string Message,
    DohProviderSwitchOutcome SwitchOutcome = DohProviderSwitchOutcome.AppliedNew);
