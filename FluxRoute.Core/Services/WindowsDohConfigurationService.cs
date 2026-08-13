using System.Security.Principal;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public interface IWindowsDohConfigurationService
{
    Task<DohApplyResult> ApplyAsync(
        string interfaceName,
        DohProvider provider,
        DohEncryptionMode mode = DohEncryptionMode.EncryptedOnly,
        CancellationToken ct = default);

    Task<DohApplyResult> DisableAsync(
        string interfaceName,
        CancellationToken ct = default);

    Task<DohApplyResult> RestoreAsync(
        string interfaceName,
        IReadOnlyList<string> dnsAddresses,
        bool wasDhcp,
        CancellationToken ct = default);

    Task<DohApplyResult> RecoverPendingOperationAsync(CancellationToken ct = default);
}

/// <summary>Настраивает встроенный Windows DNS-клиент через документированный netsh dnsclient.</summary>
public sealed class WindowsDohConfigurationService : IWindowsDohConfigurationService
{
    private readonly IProcessRunner _processRunner;
    private readonly IDnsAdapterService _dnsAdapterService;
    private readonly IDohSystemConfigurationService _dohSystemConfiguration;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<WindowsDohConfigurationService> _logger;
    private readonly Func<bool> _isWindows;
    private readonly Func<bool> _isAdministrator;
    private readonly Dictionary<string, DnsAdapterSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    public WindowsDohConfigurationService(
        IProcessRunner processRunner,
        IDnsAdapterService dnsAdapterService,
        IDohSystemConfigurationService dohSystemConfiguration,
        ISettingsService settingsService,
        ILogger<WindowsDohConfigurationService> logger,
        Func<bool>? isWindows = null,
        Func<bool>? isAdministrator = null)
    {
        _processRunner = processRunner;
        _dnsAdapterService = dnsAdapterService;
        _dohSystemConfiguration = dohSystemConfiguration;
        _settingsService = settingsService;
        _logger = logger;
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
        _isAdministrator = isAdministrator ?? IsAdministrator;
    }

    public async Task<DohApplyResult> ApplyAsync(
        string interfaceName,
        DohProvider provider,
        DohEncryptionMode mode = DohEncryptionMode.EncryptedOnly,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(provider);
        var validation = ValidateEnvironment();
        if (validation is not null) return validation;
        if (provider.DnsAddresses.Count == 0) return new(false, "У провайдера не указаны DNS-адреса.");

        var capability = await RunNetshAsync(["dnsclient", "show", "encryption"], ct).ConfigureAwait(false);
        if (capability.ExitCode != 0)
            return new(false, "Системный DNS-over-HTTPS не поддерживается этой версией Windows.");

        var recovery = await RecoverPendingOperationAsync(ct).ConfigureAwait(false);
        if (!recovery.IsSuccess) return recovery;
        var persisted = _settingsService.Load();

        var adapter = await _dnsAdapterService.CaptureAsync(interfaceName, ct).ConfigureAwait(false);
        if (adapter is null) return new(false, "Выбранный сетевой адаптер не найден.");
        var system = await _dohSystemConfiguration
            .CaptureSystemStateAsync(adapter.InterfaceId, provider.DnsAddresses, ct).ConfigureAwait(false);
        var journal = new DohOperationJournal
        {
            OperationId = Guid.NewGuid(),
            InterfaceName = adapter.InterfaceName,
            InterfaceId = adapter.InterfaceId,
            Ipv4 = adapter.Ipv4State,
            Ipv6 = adapter.Ipv6State,
            GlobalDohMode = system.GlobalDohMode,
            ResolverMappings = system.ResolverMappings.ToList(),
            InterfaceMappings = system.InterfaceMappings.ToList()
        };
        persisted = _settingsService.Load();
        persisted.Doh.OperationJournal = journal;
        _settingsService.Save(persisted);

        async Task<DohApplyResult> RollbackFailureAsync(DohApplyResult failure)
        {
            var rollback = await _dohSystemConfiguration.RestoreSystemStateAsync(journal, CancellationToken.None).ConfigureAwait(false);
            if (rollback.IsSuccess)
            {
                var settings = _settingsService.Load();
                settings.Doh.OperationJournal = null;
                _settingsService.Save(settings);
                return failure;
            }
            return new(false, $"{failure.Message} Откат не завершён: {rollback.Message}");
        }

        try
        {
            if (mode != DohEncryptionMode.Unencrypted)
            {
                var global = await RunNetshAsync(["dnsclient", "set", "global", "doh=auto"], ct).ConfigureAwait(false);
                if (global.ExitCode != 0)
                    return await RollbackFailureAsync(new(false, BuildFailureMessage("Не удалось включить системный DNS-over-HTTPS", global))).ConfigureAwait(false);
            }

            var configured = await _dohSystemConfiguration.ConfigureAsync(provider, mode, ct).ConfigureAwait(false);
            if (!configured.IsSuccess) return await RollbackFailureAsync(configured).ConfigureAwait(false);

            if (mode == DohEncryptionMode.Unencrypted)
            {
                foreach (var address in provider.DnsAddresses)
                {
                    var encryption = await RunNetshAsync([
                        "dnsclient", "set", "encryption", $"server={address}",
                        $"dohtemplate={provider.Template}", "autoupgrade=no", "udpfallback=yes"], ct).ConfigureAwait(false);
                    if (encryption.ExitCode != 0)
                        return await RollbackFailureAsync(new(false, BuildFailureMessage("Не удалось отключить автоматическое DoH-обновление", encryption))).ConfigureAwait(false);
                }
            }

            var interfaceMode = await _dohSystemConfiguration
                .ConfigureInterfaceModeAsync(adapter.InterfaceId, provider, mode, ct).ConfigureAwait(false);
            if (!interfaceMode.IsSuccess) return await RollbackFailureAsync(interfaceMode).ConfigureAwait(false);

            var apply = await _dohSystemConfiguration.ConfigureAdapterAsync(interfaceName, provider.DnsAddresses, ct).ConfigureAwait(false);
            if (!apply.IsSuccess) return await RollbackFailureAsync(apply).ConfigureAwait(false);
            await FlushDnsAsync(ct).ConfigureAwait(false);

            if (!await _dohSystemConfiguration.VerifyAdapterAsync(interfaceName, provider.DnsAddresses, ct).ConfigureAwait(false))
                return await RollbackFailureAsync(new(false, "Windows не подтвердила новые DNS-адреса.")).ConfigureAwait(false);
            if (mode != DohEncryptionMode.Unencrypted
                && !await _dohSystemConfiguration.VerifyAsync(provider, mode, ct).ConfigureAwait(false))
                return await RollbackFailureAsync(new(false, "Windows не подтвердила выбранный режим шифрования.")).ConfigureAwait(false);
            if (!await _dohSystemConfiguration.VerifyInterfaceModeAsync(adapter.InterfaceId, provider, mode, ct).ConfigureAwait(false))
                return await RollbackFailureAsync(new(false, "Windows не подтвердила режим DoH выбранного интерфейса.")).ConfigureAwait(false);

            var settings = _settingsService.Load();
            settings.Doh.OperationJournal = null;
            _settingsService.Save(settings);
            _logger.LogInformation("DoH-провайдер {Provider} применён к интерфейсу {Interface}", provider.Name, interfaceName);
            return new(true, $"DoH {provider.Name} включён для «{interfaceName}».");
        }
        catch (Exception ex)
        {
            var rollback = await _dohSystemConfiguration
                .RestoreSystemStateAsync(journal, CancellationToken.None).ConfigureAwait(false);
            if (!rollback.IsSuccess)
                throw new InvalidOperationException(
                    $"Применение DoH завершилось ошибкой, откат также не выполнен: {rollback.Message}", ex);
            ClearOperationJournal();
            throw;
        }
    }

    public async Task<DohApplyResult> DisableAsync(string interfaceName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        var validation = ValidateEnvironment();
        if (validation is not null) return validation;
        var recovery = await RecoverPendingOperationAsync(ct).ConfigureAwait(false);
        if (!recovery.IsSuccess) return recovery;

        var adapter = await _dnsAdapterService.CaptureAsync(interfaceName, ct).ConfigureAwait(false);
        if (adapter is null) return new(false, "Выбранный сетевой адаптер не найден.");
        var system = await _dohSystemConfiguration
            .CaptureSystemStateAsync(adapter.InterfaceId, adapter.DnsAddresses, ct).ConfigureAwait(false);
        var journal = new DohOperationJournal
        {
            OperationId = Guid.NewGuid(), InterfaceName = adapter.InterfaceName, InterfaceId = adapter.InterfaceId,
            Ipv4 = adapter.Ipv4State, Ipv6 = adapter.Ipv6State, GlobalDohMode = system.GlobalDohMode,
            ResolverMappings = system.ResolverMappings.ToList(), InterfaceMappings = system.InterfaceMappings.ToList()
        };
        var settings = _settingsService.Load();
        settings.Doh.OperationJournal = journal;
        _settingsService.Save(settings);

        var disabledState = new DohOperationJournal
        {
            OperationId = journal.OperationId,
            InterfaceName = journal.InterfaceName,
            InterfaceId = journal.InterfaceId,
            Ipv4 = new DnsFamilySnapshot(true, []),
            Ipv6 = new DnsFamilySnapshot(true, []),
            GlobalDohMode = DohGlobalMode.Disabled,
            ResolverMappings = journal.ResolverMappings.Select(x => x with
            {
                Exists = false, Template = null, AutoUpgrade = false, AllowFallbackToUdp = false
            }).ToList(),
            InterfaceMappings = journal.InterfaceMappings.Select(x => x with
            {
                Exists = false, Template = null, Flags = null
            }).ToList()
        };

        async Task<DohApplyResult> RollbackAsync(DohApplyResult failure)
        {
            var rollback = await _dohSystemConfiguration
                .RestoreSystemStateAsync(journal, CancellationToken.None).ConfigureAwait(false);
            if (!rollback.IsSuccess)
                return new(false, $"{failure.Message} Откат не завершён: {rollback.Message}");
            ClearOperationJournal();
            return failure;
        }

        try
        {
            var disable = await _dohSystemConfiguration
                .RestoreSystemStateAsync(disabledState, ct).ConfigureAwait(false);
            if (!disable.IsSuccess) return await RollbackAsync(disable).ConfigureAwait(false);
            await FlushDnsAsync(ct).ConfigureAwait(false);
            if (!await _dohSystemConfiguration.VerifySystemStateAsync(disabledState, ct).ConfigureAwait(false))
                return await RollbackAsync(new(false, "Windows не подтвердила отключённое состояние всех слоёв DNS/DoH.")).ConfigureAwait(false);

            settings = _settingsService.Load();
            settings.Doh.OperationJournal = null;
            _settingsService.Save(settings);
            _snapshots.Remove(interfaceName);
            _logger.LogInformation("DoH отключён и DNS от DHCP восстановлен для интерфейса {Interface}", interfaceName);
            return new(true, $"Для «{interfaceName}» отключён DoH и восстановлен DNS от DHCP.");
        }
        catch (Exception ex)
        {
            var rollback = await _dohSystemConfiguration
                .RestoreSystemStateAsync(journal, CancellationToken.None).ConfigureAwait(false);
            if (!rollback.IsSuccess)
                throw new InvalidOperationException(
                    $"Отключение DoH завершилось ошибкой, откат также не выполнен: {rollback.Message}", ex);
            ClearOperationJournal();
            throw;
        }
    }

    public async Task<DohApplyResult> RestoreAsync(
        string interfaceName,
        IReadOnlyList<string> dnsAddresses,
        bool wasDhcp,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(dnsAddresses);

        var validation = ValidateEnvironment();
        if (validation is not null)
        {
            return validation;
        }

        var result = await RestoreSnapshotAsync(
            new DnsAdapterSnapshot(interfaceName, dnsAddresses, wasDhcp), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await FlushDnsAsync(ct).ConfigureAwait(false);
            return new DohApplyResult(true, $"Для «{interfaceName}» восстановлена предыдущая DNS-конфигурация.");
        }

        return result;
    }

    public async Task<DohApplyResult> RecoverPendingOperationAsync(CancellationToken ct = default)
    {
        var settings = _settingsService.Load();
        if (settings.Doh.OperationJournal is not { } pending)
            return new(true, "Незавершённых операций DoH нет.");

        var recovery = await _dohSystemConfiguration
            .RestoreSystemStateAsync(pending, CancellationToken.None).ConfigureAwait(false);
        if (!recovery.IsSuccess)
            return new(false, "Не удалось восстановить незавершённую операцию DoH: " + recovery.Message);

        settings = _settingsService.Load();
        settings.Doh.OperationJournal = null;
        _settingsService.Save(settings);
        return new(true, "Незавершённая операция DoH восстановлена.");
    }

    private async Task<DohApplyResult> SetDnsAddressesAsync(
        string interfaceName,
        IReadOnlyList<string> addresses,
        CancellationToken ct)
    {
        if (addresses.Count == 0)
        {
            return new DohApplyResult(
                false,
                "Снимок не содержит DNS-адресов; безопасный автоматический откат невозможен.");
        }

        for (var index = 0; index < addresses.Count; index++)
        {
            var command = index == 0 ? "set" : "add";
            var arguments = new List<string>
            {
                "interface", "ip", command, "dnsservers",
                $"name={interfaceName}",
                $"address={addresses[index]}"
            };
            if (index == 0)
            {
                arguments.Add("source=static");
            }
            if (index > 0)
            {
                arguments.Add($"index={index + 1}");
            }

            var dnsResult = await RunNetshAsync(arguments, ct).ConfigureAwait(false);
            if (dnsResult.ExitCode != 0)
            {
                return new DohApplyResult(
                    false,
                    BuildFailureMessage("Не удалось изменить DNS сетевого адаптера", dnsResult));
            }
        }

        return new DohApplyResult(true, "DNS-адреса применены.");
    }

    private async Task<DohApplyResult> RestoreSnapshotAsync(
        DnsAdapterSnapshot snapshot,
        CancellationToken ct)
    {
        if (snapshot.IsDhcp)
        {
            var dhcpResult = await RunNetshAsync(
                ["interface", "ip", "set", "dnsservers", $"name={snapshot.InterfaceName}", "source=dhcp"],
                ct).ConfigureAwait(false);
            return dhcpResult.ExitCode == 0
                ? new DohApplyResult(true, $"Для «{snapshot.InterfaceName}» восстановлен DNS от DHCP.")
                : new DohApplyResult(false, BuildFailureMessage("Не удалось восстановить DNS от DHCP", dhcpResult));
        }

        var result = await SetDnsAddressesAsync(
            snapshot.InterfaceName, snapshot.DnsAddresses, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Не удалось восстановить DNS интерфейса {Interface}: {Error}",
                snapshot.InterfaceName,
                result.Message);
            return result;
        }

        return new DohApplyResult(
            true,
            $"Для «{snapshot.InterfaceName}» восстановлена предыдущая DNS-конфигурация.");
    }

    private DohApplyResult? ValidateEnvironment()
    {
        if (!_isWindows())
        {
            return new DohApplyResult(false, "Системная настройка DoH доступна только в Windows.");
        }

        if (!_isAdministrator())
        {
            return new DohApplyResult(false, "Для изменения DNS требуются права администратора.");
        }

        return null;
    }

    private Task<ProcessRunResult> RunNetshAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        return _processRunner.RunAsync("netsh.exe", arguments, ct);
    }

    private void ClearOperationJournal()
    {
        var settings = _settingsService.Load();
        settings.Doh.OperationJournal = null;
        _settingsService.Save(settings);
    }

    private async Task FlushDnsAsync(CancellationToken ct)
    {
        var result = await _processRunner
            .RunAsync("ipconfig.exe", ["/flushdns"], ct)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _logger.LogWarning("Не удалось очистить DNS-кэш: {Error}", result.StandardError);
        }
    }

    private static string BuildFailureMessage(string prefix, ProcessRunResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}: {detail}";
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}


