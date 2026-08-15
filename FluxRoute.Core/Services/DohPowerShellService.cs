using System.Text.Json;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public interface IDohSystemConfigurationService
{
    Task<DohApplyResult> ConfigureAsync(
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);

    Task<bool> VerifyAsync(
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);

    Task<DohApplyResult> ConfigureAdapterAsync(
        string interfaceName,
        IReadOnlyList<string> dnsAddresses,
        CancellationToken ct = default);

    Task<DohApplyResult> ConfigureInterfaceModeAsync(
        string interfaceId,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);

    Task<bool> VerifyAdapterAsync(
        string interfaceName,
        IReadOnlyList<string> expectedAddresses,
        CancellationToken ct = default);

    Task<bool> VerifyInterfaceModeAsync(
        string interfaceId,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default);

    Task<DohSystemStateSnapshot> CaptureSystemStateAsync(
        string interfaceId,
        IReadOnlyList<string> addresses,
        CancellationToken ct = default);

    Task<DohApplyResult> RestoreSystemStateAsync(
        DohOperationJournal journal,
        CancellationToken ct = default);

    Task<bool> VerifySystemStateAsync(
        DohOperationJournal journal,
        CancellationToken ct = default);
}

/// <summary>Применяет и проверяет DoH через структурированные DnsClient cmdlets.</summary>
public sealed class DohPowerShellService : IDohSystemConfigurationService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<DohPowerShellService> _logger;

    public DohPowerShellService(
        IProcessRunner processRunner,
        ILogger<DohPowerShellService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<DohApplyResult> ConfigureAsync(
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var providerValidation = ValidateProvider(provider);
        if (providerValidation is not null)
        {
            return providerValidation;
        }

        if (mode == DohEncryptionMode.Unencrypted)
        {
            return new DohApplyResult(true, "Незашифрованный DNS выбран.");
        }

        var fallback = mode == DohEncryptionMode.EncryptedWithFallback ? "True" : "False";
        foreach (var address in provider.DnsAddresses)
        {
            var escapedAddress = Escape(address);
            var escapedTemplate = Escape(provider.Template.AbsoluteUri);
            var script = "$ErrorActionPreference='Stop'; "
                + "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
                + "$OutputEncoding = [Console]::OutputEncoding; "
                + $"$existing = Get-DnsClientDohServerAddress -ServerAddress '{escapedAddress}' -ErrorAction SilentlyContinue; "
                + "if ($null -eq $existing) { "
                + $"Add-DnsClientDohServerAddress -ServerAddress '{escapedAddress}' "
                + $"-DohTemplate '{escapedTemplate}' -AutoUpgrade $True -AllowFallbackToUdp ${fallback} "
                + "} else { "
                + $"Set-DnsClientDohServerAddress -ServerAddress '{escapedAddress}' "
                + $"-DohTemplate '{escapedTemplate}' -AutoUpgrade $True -AllowFallbackToUdp ${fallback} "
                + "}";
            var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _logger.LogError(
                    "Не удалось применить DoH через DnsClient для {Address}: {Error}",
                    address,
                    result.StandardError);
                return new DohApplyResult(false, "Windows DnsClient не принял параметры DoH: " + result.StandardError.Trim());
            }
        }

        return new DohApplyResult(true, "Параметры DoH применены через Windows DnsClient.");
    }

    public async Task<bool> VerifyAsync(
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (ValidateProvider(provider) is not null)
        {
            return false;
        }

        if (mode == DohEncryptionMode.Unencrypted)
        {
            return true;
        }

        var joinedAddresses = string.Join(",", provider.DnsAddresses.Select(address => $"'{Escape(address)}'"));
        var script = "$ErrorActionPreference='Stop'; "
            + $"@(Get-DnsClientDohServerAddress -ServerAddress {joinedAddresses} | "
            + "Select-Object ServerAddress,DohTemplate,AutoUpgrade,AllowFallbackToUdp) | "
            + "ConvertTo-Json -Compress";
        var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return false;
        }

        try
        {
            var records = JsonSerializer.Deserialize<List<DohState>>(result.StandardOutput)
                ?? [];
            var expectedFallback = mode == DohEncryptionMode.EncryptedWithFallback;
            return provider.DnsAddresses.All(address => records.Any(record =>
                string.Equals(record.ServerAddress, address, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.DohTemplate, provider.Template.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                && record.AutoUpgrade
                && record.AllowFallbackToUdp == expectedFallback));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Не удалось разобрать состояние Windows DnsClient");
            return false;
        }
    }

    public async Task<DohApplyResult> ConfigureAdapterAsync(
        string interfaceName,
        IReadOnlyList<string> dnsAddresses,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(dnsAddresses);
        if (dnsAddresses.Count == 0)
        {
            return new DohApplyResult(false, "Не указаны DNS-адреса для сетевого адаптера.");
        }

        var parsedAddresses = new List<System.Net.IPAddress>(dnsAddresses.Count);
        foreach (var address in dnsAddresses)
        {
            if (!System.Net.IPAddress.TryParse(address, out var parsedAddress))
            {
                return new DohApplyResult(false, $"Некорректный DNS-адрес: {address}");
            }

            parsedAddresses.Add(parsedAddress);
        }

        var ipv4 = parsedAddresses
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(address => address.ToString())
            .ToArray();
        var ipv6 = parsedAddresses
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            .Select(address => address.ToString())
            .ToArray();

        var ipv4Result = await ConfigureAddressFamilyAsync(
            "ipv4", interfaceName, ipv4, ct).ConfigureAwait(false);
        if (!ipv4Result.IsSuccess)
        {
            return ipv4Result;
        }

        var ipv6Result = await ConfigureAddressFamilyAsync(
            "ipv6", interfaceName, ipv6, ct).ConfigureAwait(false);
        return ipv6Result.IsSuccess
            ? new DohApplyResult(true, "IPv4 и IPv6 DNS-адреса адаптера применены раздельно.")
            : ipv6Result;
    }

    public async Task<DohApplyResult> ConfigureInterfaceModeAsync(
        string interfaceId,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceId);
        ArgumentNullException.ThrowIfNull(provider);
        if (!Guid.TryParse(interfaceId, out var parsedInterfaceId))
        {
            return new DohApplyResult(false, "Некорректный идентификатор сетевого адаптера.");
        }

        var flags = mode switch
        {
            DohEncryptionMode.EncryptedOnly => 17,
            DohEncryptionMode.EncryptedWithFallback => 21,
            _ => 0
        };
        var root = $"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\{{{parsedInterfaceId:D}}}\\DohInterfaceSettings";
        var commands = new List<string> { "$ErrorActionPreference='Stop'" };

        foreach (var address in provider.DnsAddresses)
        {
            if (!System.Net.IPAddress.TryParse(address, out var parsedAddress))
            {
                return new DohApplyResult(false, $"Некорректный DNS-адрес: {address}");
            }

            var addressFamilyKey = parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? "Doh6"
                : "Doh";
            var path = $"{root}\\{addressFamilyKey}\\{address}";
            if (mode == DohEncryptionMode.Unencrypted)
            {
                commands.Add($"if (Test-Path '{Escape(path)}') {{ Remove-Item '{Escape(path)}' -Recurse -Force }}");
                continue;
            }

            commands.Add($"New-Item -Path '{Escape(path)}' -Force | Out-Null");
            commands.Add($"New-ItemProperty -Path '{Escape(path)}' -Name DohTemplate -Value '{Escape(provider.Template.AbsoluteUri)}' -PropertyType String -Force | Out-Null");
            commands.Add($"New-ItemProperty -Path '{Escape(path)}' -Name DohFlags -Value {flags} -PropertyType QWord -Force | Out-Null");
        }

        var result = await RunPowerShellAsync(string.Join("; ", commands), ct).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new DohApplyResult(true, "Режим DoH сетевого адаптера применён.")
            : new DohApplyResult(false, "Не удалось применить режим DoH адаптера: " + result.StandardError.Trim());
    }

    public async Task<bool> VerifyAdapterAsync(
        string interfaceName,
        IReadOnlyList<string> expectedAddresses,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(expectedAddresses);

        var expectedIpv4 = ToAddressSet(expectedAddresses, System.Net.Sockets.AddressFamily.InterNetwork);
        var expectedIpv6 = ToAddressSet(expectedAddresses, System.Net.Sockets.AddressFamily.InterNetworkV6);
        var script = "$ErrorActionPreference='Stop'; "
            + $"@(Get-DnsClientServerAddress -InterfaceAlias '{Escape(interfaceName)}' | "
            + "Select-Object AddressFamily,ServerAddresses) | ConvertTo-Json -Compress";

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
            if (result.ExitCode == 0 && TryParseAdapterState(result.StandardOutput, out var states))
            {
                var actualIpv4 = ToAddressSet(
                    states.Where(state => state.AddressFamily == 2).SelectMany(state => state.ServerAddresses),
                    System.Net.Sockets.AddressFamily.InterNetwork);
                var actualIpv6 = ToAddressSet(
                    states.Where(state => state.AddressFamily == 23).SelectMany(state => state.ServerAddresses),
                    System.Net.Sockets.AddressFamily.InterNetworkV6);

                if (expectedIpv4.SetEquals(actualIpv4) && expectedIpv6.SetEquals(actualIpv6))
                {
                    return true;
                }
            }

            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    public async Task<bool> VerifyInterfaceModeAsync(
        string interfaceId,
        DohProvider provider,
        DohEncryptionMode mode,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(interfaceId, out var parsedInterfaceId) || ValidateProvider(provider) is not null)
        {
            return false;
        }

        var root = $"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\{{{parsedInterfaceId:D}}}\\DohInterfaceSettings";
        var selections = provider.DnsAddresses.Select(address =>
        {
            var parsed = System.Net.IPAddress.Parse(address);
            var isIpv6 = parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            var branch = isIpv6 ? "Doh6" : "Doh";
            var path = $"{root}\\{branch}\\{address}";
            return $"$p='{Escape(path)}'; if(Test-Path $p){{$v=Get-ItemProperty $p;[pscustomobject]@{{Address='{Escape(address)}';IsIpv6=${isIpv6.ToString().ToLowerInvariant()};Exists=$true;Template=$v.DohTemplate;Flags=[long]$v.DohFlags}}}}else{{[pscustomobject]@{{Address='{Escape(address)}';IsIpv6=${isIpv6.ToString().ToLowerInvariant()};Exists=$false;Template=$null;Flags=$null}}}}";
        });
        var result = await RunPowerShellAsync(
            "$ErrorActionPreference='Stop'; @(" + string.Join(";", selections) + ") | ConvertTo-Json -Compress",
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return false;
        }

        try
        {
            var states = JsonSerializer.Deserialize<List<DohInterfaceState>>(result.StandardOutput) ?? [];
            if (mode == DohEncryptionMode.Unencrypted)
            {
                return provider.DnsAddresses.All(address => states.Any(s =>
                    string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase) && !s.Exists));
            }

            var expectedFlags = mode == DohEncryptionMode.EncryptedOnly ? 17 : 21;
            return provider.DnsAddresses.All(address => states.Any(s =>
                string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase)
                && s.Exists && s.Flags == expectedFlags
                && string.Equals(s.Template, provider.Template.AbsoluteUri, StringComparison.OrdinalIgnoreCase)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<DohSystemStateSnapshot> CaptureSystemStateAsync(
        string interfaceId,
        IReadOnlyList<string> addresses,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceId);
        ArgumentNullException.ThrowIfNull(addresses);
        if (!Guid.TryParse(interfaceId, out var parsedInterfaceId))
            throw new ArgumentException("Некорректный идентификатор сетевого адаптера.", nameof(interfaceId));

        var globalScript = "$ErrorActionPreference='Stop'; "
            + "$raw = & netsh.exe dnsclient show global 2>&1; "
            + "if ($LASTEXITCODE -ne 0) { throw ($raw -join [Environment]::NewLine) }; "
            + "$mode = @($raw | ForEach-Object { if ($_ -match '(?i)\\bDoH\\b.*?(?:[:=]\\s*|\\b(?:global\\s+)?(?:setting|mode)\\b\\s*[:=]\\s*)(disabled|enabled|automatic|auto|yes|no)\\b') { switch ($Matches[1].ToLowerInvariant()) { 'disabled' { 'no'; break } 'enabled' { 'yes'; break } 'automatic' { 'auto'; break } default { $Matches[1].ToLowerInvariant() } } } } | Select-Object -Unique); "
            + "if ($mode.Count -ne 1) { $mode = @('__unknown__') }; "
            + "[pscustomobject]@{Mode=$mode[0]} | ConvertTo-Json -Compress";
        var global = await RunPowerShellAsync(globalScript, ct).ConfigureAwait(false);
        if (global.ExitCode != 0)
            throw new InvalidOperationException("Не удалось прочитать глобальный режим DoH: " + global.StandardError.Trim());

        var resolverMappings = new List<DohResolverMappingSnapshot>();
        var interfaceMappings = new List<DohInterfaceMappingSnapshot>();
        var root = $"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\{{{parsedInterfaceId:D}}}\\DohInterfaceSettings";
        foreach (var address in addresses)
        {
            if (!System.Net.IPAddress.TryParse(address, out var parsedAddress))
                throw new ArgumentException($"Некорректный DNS-адрес: {address}", nameof(addresses));

            var mappingResult = await RunPowerShellAsync(
                "$ErrorActionPreference='Stop'; " +
                $"$v=Get-DnsClientDohServerAddress -ServerAddress '{Escape(address)}' -ErrorAction SilentlyContinue; " +
                "if($null -eq $v){'[null]'}else{$v | Select-Object ServerAddress,DohTemplate,AutoUpgrade,AllowFallbackToUdp | ConvertTo-Json -Compress}", ct).ConfigureAwait(false);
            if (mappingResult.ExitCode != 0)
                throw new InvalidOperationException("Не удалось прочитать сопоставление DoH: " + mappingResult.StandardError.Trim());
            if (mappingResult.StandardOutput.Trim() == "[null]")
                resolverMappings.Add(new(address, false, null, false, false));
            else
            {
                var state = JsonSerializer.Deserialize<DohState>(mappingResult.StandardOutput)
                    ?? throw new InvalidOperationException("Windows вернула пустое сопоставление DoH.");
                resolverMappings.Add(new(address, true, state.DohTemplate, state.AutoUpgrade, state.AllowFallbackToUdp));
            }

            var isIpv6 = parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            var path = $"{root}\\{(isIpv6 ? "Doh6" : "Doh")}\\{address}";
            var interfaceResult = await RunPowerShellAsync(
                "$ErrorActionPreference='Stop'; " +
                $"$p='{Escape(path)}'; if(Test-Path $p){{$v=Get-ItemProperty $p;[pscustomobject]@{{Address='{Escape(address)}';IsIpv6=${isIpv6.ToString().ToLowerInvariant()};Exists=$true;Template=$v.DohTemplate;Flags=[long]$v.DohFlags}} | ConvertTo-Json -Compress}}else{{'[null]'}}", ct).ConfigureAwait(false);
            if (interfaceResult.ExitCode != 0)
                throw new InvalidOperationException("Не удалось прочитать интерфейсное состояние DoH: " + interfaceResult.StandardError.Trim());
            interfaceMappings.Add(interfaceResult.StandardOutput.Trim() == "[null]"
                ? new(address, isIpv6, false, null, null)
                : JsonSerializer.Deserialize<DohInterfaceMappingSnapshot>(interfaceResult.StandardOutput)
                    ?? throw new InvalidOperationException("Windows вернула пустое интерфейсное состояние DoH."));
        }
        return new(ParseGlobalMode(global.StandardOutput), resolverMappings, interfaceMappings);
    }

    public async Task<DohApplyResult> RestoreSystemStateAsync(DohOperationJournal journal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var dns4 = await RestoreFamilyAsync("ipv4", journal.InterfaceName, journal.Ipv4, ct).ConfigureAwait(false);
        if (!dns4.IsSuccess) return dns4;
        var dns6 = await RestoreFamilyAsync("ipv6", journal.InterfaceName, journal.Ipv6, ct).ConfigureAwait(false);
        if (!dns6.IsSuccess) return dns6;

        foreach (var mapping in journal.ResolverMappings)
        {
            var script = !mapping.Exists
                ? $"$v=Get-DnsClientDohServerAddress -ServerAddress '{Escape(mapping.Address)}' -ErrorAction SilentlyContinue; if($null -ne $v){{Remove-DnsClientDohServerAddress -ServerAddress '{Escape(mapping.Address)}' -Confirm:$false}}"
                : $"$existing=Get-DnsClientDohServerAddress -ServerAddress '{Escape(mapping.Address)}' -ErrorAction SilentlyContinue; if($null -eq $existing){{Add-DnsClientDohServerAddress -ServerAddress '{Escape(mapping.Address)}' -DohTemplate '{Escape(mapping.Template ?? string.Empty)}' -AutoUpgrade ${mapping.AutoUpgrade} -AllowFallbackToUdp ${mapping.AllowFallbackToUdp}}}else{{Set-DnsClientDohServerAddress -ServerAddress '{Escape(mapping.Address)}' -DohTemplate '{Escape(mapping.Template ?? string.Empty)}' -AutoUpgrade ${mapping.AutoUpgrade} -AllowFallbackToUdp ${mapping.AllowFallbackToUdp}}}";
            var result = await RunPowerShellAsync("$ErrorActionPreference='Stop'; " + script, ct).ConfigureAwait(false);
            if (result.ExitCode != 0) return new(false, "Не удалось восстановить сопоставления DoH: " + result.StandardError.Trim());
        }

        if (Guid.TryParse(journal.InterfaceId, out var interfaceId))
        {
            var root = $"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\{{{interfaceId:D}}}\\DohInterfaceSettings";
            foreach (var mapping in journal.InterfaceMappings)
            {
                var path = $"{root}\\{(mapping.IsIpv6 ? "Doh6" : "Doh")}\\{mapping.Address}";
                var script = mapping.Exists
                    ? $"New-Item -Path '{Escape(path)}' -Force|Out-Null; New-ItemProperty -Path '{Escape(path)}' -Name DohTemplate -Value '{Escape(mapping.Template ?? string.Empty)}' -PropertyType String -Force|Out-Null; New-ItemProperty -Path '{Escape(path)}' -Name DohFlags -Value {mapping.Flags ?? 0} -PropertyType QWord -Force|Out-Null"
                    : $"if(Test-Path '{Escape(path)}'){{Remove-Item '{Escape(path)}' -Recurse -Force}}";
                var result = await RunPowerShellAsync("$ErrorActionPreference='Stop'; " + script, ct).ConfigureAwait(false);
                if (result.ExitCode != 0) return new(false, "Не удалось восстановить интерфейсное состояние DoH: " + result.StandardError.Trim());
            }
        }

        var global = await _processRunner.RunAsync("netsh.exe",
            ["dnsclient", "set", "global", $"doh={ToGlobalModeValue(journal.GlobalDohMode)}"], ct).ConfigureAwait(false);
        if (global.ExitCode != 0)
            return new(false, "Не удалось восстановить глобальный режим DoH: " + global.StandardError.Trim());

        return await VerifySystemStateAsync(journal, ct).ConfigureAwait(false)
            ? new(true, "Системное состояние DNS восстановлено и проверено.")
            : new(false, "Windows не подтвердила восстановление всех слоёв DNS/DoH.");
    }

    public async Task<bool> VerifySystemStateAsync(DohOperationJournal journal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var familyState = await CaptureDnsFamilyStatesAsync(journal.InterfaceName, ct).ConfigureAwait(false);
        if (familyState is null
            || familyState.Value.Ipv4.IsDhcp != journal.Ipv4.IsDhcp
            || familyState.Value.Ipv6.IsDhcp != journal.Ipv6.IsDhcp
            || (!journal.Ipv4.IsDhcp
                && !ToAddressSet(familyState.Value.Ipv4.Addresses, System.Net.Sockets.AddressFamily.InterNetwork)
                    .SetEquals(ToAddressSet(journal.Ipv4.Addresses, System.Net.Sockets.AddressFamily.InterNetwork)))
            || (!journal.Ipv6.IsDhcp
                && !ToAddressSet(familyState.Value.Ipv6.Addresses, System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .SetEquals(ToAddressSet(journal.Ipv6.Addresses, System.Net.Sockets.AddressFamily.InterNetworkV6))))
            return false;

        var addresses = journal.ResolverMappings.Select(x => x.Address)
            .Concat(journal.InterfaceMappings.Select(x => x.Address))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = await CaptureSystemStateAsync(journal.InterfaceId, addresses, ct).ConfigureAwait(false);
        return actual.GlobalDohMode == journal.GlobalDohMode
            && actual.ResolverMappings.SequenceEqual(journal.ResolverMappings)
            && actual.InterfaceMappings.SequenceEqual(journal.InterfaceMappings);
    }

    private async Task<(DnsFamilySnapshot Ipv4, DnsFamilySnapshot Ipv6)?> CaptureDnsFamilyStatesAsync(
        string interfaceName,
        CancellationToken ct)
    {
        var script = "$ErrorActionPreference='Stop'; "
            + $"@(Get-DnsClientServerAddress -InterfaceAlias '{Escape(interfaceName)}' | "
            + "Select-Object AddressFamily,ServerAddresses,AddressOrigin) | ConvertTo-Json -Compress";
        var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        try
        {
            var states = JsonSerializer.Deserialize<List<DnsAdapterState>>(result.StandardOutput) ?? [];
            DnsFamilySnapshot Snapshot(int family) => new(
                states.Where(x => x.AddressFamily == family).All(x =>
                    string.Equals(x.AddressOrigin, "Dhcp", StringComparison.OrdinalIgnoreCase)),
                states.Where(x => x.AddressFamily == family).SelectMany(x => x.ServerAddresses).ToArray());
            return (Snapshot(2), Snapshot(23));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<DohApplyResult> RestoreFamilyAsync(string family, string interfaceName, DnsFamilySnapshot snapshot, CancellationToken ct) =>
        ConfigureAddressFamilyAsync(family, interfaceName, snapshot.IsDhcp ? [] : snapshot.Addresses, ct);

    private static DohGlobalMode ParseGlobalMode(string output)
    {
        try
        {
            var state = JsonSerializer.Deserialize<DohGlobalState>(output)
                ?? throw new InvalidOperationException("Windows вернула пустой глобальный режим DoH.");
            return state.Mode.ToLowerInvariant() switch
            {
                "auto" or "automatic" => DohGlobalMode.Automatic,
                "yes" or "enabled" => DohGlobalMode.Enabled,
                "no" or "disabled" => DohGlobalMode.Disabled,
                _ => throw new InvalidOperationException(
                    $"Windows вернула неизвестный глобальный режим DoH: «{state.Mode}».")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Не удалось разобрать нормализованный глобальный режим DoH.", ex);
        }
    }

    private static string ToGlobalModeValue(DohGlobalMode mode) => mode switch
    {
        DohGlobalMode.Automatic => "auto",
        DohGlobalMode.Enabled => "yes",
        _ => "no"
    };

    private static HashSet<string> ToAddressSet(
        IEnumerable<string> addresses,
        System.Net.Sockets.AddressFamily family)
    {
        return addresses
            .Select(address => System.Net.IPAddress.TryParse(address, out var parsed) ? parsed : null)
            .Where(address => address?.AddressFamily == family)
            .Select(address => address!.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseAdapterState(string json, out List<DnsAdapterState> states)
    {
        states = [];
        try
        {
            states = JsonSerializer.Deserialize<List<DnsAdapterState>>(json) ?? [];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<DohApplyResult> ConfigureAddressFamilyAsync(
        string family,
        string interfaceName,
        IReadOnlyList<string> addresses,
        CancellationToken ct)
    {
        IReadOnlyList<string> arguments;
        if (addresses.Count == 0)
        {
            arguments =
            [
                "interface", family, "set", "dnsservers",
                $"name={interfaceName}", "source=dhcp"
            ];
        }
        else
        {
            var list = new List<string>
            {
                "interface", family, "set", "dnsservers",
                $"name={interfaceName}", "source=static",
                $"address={addresses[0]}", "validate=no"
            };
            arguments = list;
        }

        var result = await _processRunner
            .RunAsync("netsh.exe", arguments, ct)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return new DohApplyResult(
                false,
                $"Windows не применил {family.ToUpperInvariant()} DNS: {result.StandardError.Trim()}");
        }

        for (var index = 1; index < addresses.Count; index++)
        {
            result = await _processRunner.RunAsync(
                "netsh.exe",
                [
                    "interface", family, "add", "dnsservers",
                    $"name={interfaceName}", $"address={addresses[index]}",
                    $"index={index + 1}", "validate=no"
                ],
                ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new DohApplyResult(
                    false,
                    $"Windows не добавил {family.ToUpperInvariant()} DNS: {result.StandardError.Trim()}");
            }
        }

        return new DohApplyResult(true, $"{family.ToUpperInvariant()} DNS применён.");
    }

    private Task<ProcessRunResult> RunPowerShellAsync(string script, CancellationToken ct)
    {
        return _processRunner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            ct);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static DohApplyResult? ValidateProvider(DohProvider provider)
    {
        if (!provider.Template.IsAbsoluteUri
            || provider.Template.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(provider.Template.UserInfo)
            || !string.IsNullOrEmpty(provider.Template.Fragment))
        {
            return new DohApplyResult(false, "DoH template должен быть абсолютным HTTPS URI без userinfo и fragment.");
        }

        if (provider.DnsAddresses.Count == 0
            || provider.DnsAddresses.Any(address => !System.Net.IPAddress.TryParse(address, out _)))
        {
            return new DohApplyResult(false, "Провайдер содержит некорректный DNS-адрес.");
        }

        return null;
    }

    private sealed record DohGlobalState(string Mode);

    private sealed record DohState(
        string ServerAddress,
        string DohTemplate,
        bool AutoUpgrade,
        bool AllowFallbackToUdp);

    private sealed record DnsAdapterState(
        int AddressFamily,
        IReadOnlyList<string> ServerAddresses,
        string? AddressOrigin = null);

    private sealed record DohInterfaceState(
        string Address,
        bool IsIpv6,
        bool Exists,
        string? Template,
        long? Flags);
}
