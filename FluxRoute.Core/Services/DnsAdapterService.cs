using System.Net;
using System.Net.NetworkInformation;
using FluxRoute.Core.Models;
using Microsoft.Win32;

namespace FluxRoute.Core.Services;

public sealed record DnsAdapterSnapshot(
    string InterfaceName,
    IReadOnlyList<string> DnsAddresses,
    bool IsDhcp,
    string InterfaceId = "",
    DnsFamilySnapshot? Ipv4 = null,
    DnsFamilySnapshot? Ipv6 = null)
{
    public DnsFamilySnapshot Ipv4State => Ipv4 ?? new(IsDhcp,
        DnsAddresses.Where(a => IPAddress.TryParse(a, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToArray());
    public DnsFamilySnapshot Ipv6State => Ipv6 ?? new(IsDhcp,
        DnsAddresses.Where(a => IPAddress.TryParse(a, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6).ToArray());
}

public interface IDnsAdapterService
{
    Task<DnsAdapterSnapshot?> CaptureAsync(
        string interfaceName,
        CancellationToken ct = default);

    Task<bool> VerifyDnsAddressesAsync(
        string interfaceName,
        IReadOnlyList<string> expectedAddresses,
        CancellationToken ct = default);
}

/// <summary>Читает текущее состояние DNS сетевого адаптера без системных мутаций.</summary>
public sealed class DnsAdapterService : IDnsAdapterService
{
    public Task<DnsAdapterSnapshot?> CaptureAsync(
        string interfaceName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ct.ThrowIfCancellationRequested();

        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => string.Equals(
                item.Name,
                interfaceName,
                StringComparison.OrdinalIgnoreCase));

        if (adapter is null)
        {
            return Task.FromResult<DnsAdapterSnapshot?>(null);
        }

        var addresses = adapter.GetIPProperties().DnsAddresses
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ipv4 = addresses.Where(a => IPAddress.Parse(a).AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
        var ipv6 = addresses.Where(a => IPAddress.Parse(a).AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetworkV6).ToArray();
        var ipv4Dhcp = IsDnsFromDhcp(adapter.Id, ipv6: false);
        var ipv6Dhcp = IsDnsFromDhcp(adapter.Id, ipv6: true);
        return Task.FromResult<DnsAdapterSnapshot?>(new DnsAdapterSnapshot(
            adapter.Name, addresses, ipv4Dhcp && ipv6Dhcp, adapter.Id,
            new DnsFamilySnapshot(ipv4Dhcp, ipv4), new DnsFamilySnapshot(ipv6Dhcp, ipv6)));
    }

    public async Task<bool> VerifyDnsAddressesAsync(
        string interfaceName,
        IReadOnlyList<string> expectedAddresses,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedAddresses);

        var snapshot = await CaptureAsync(interfaceName, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return false;
        }

        return snapshot.DnsAddresses.SequenceEqual(
            expectedAddresses,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDnsFromDhcp(string interfaceId, bool ipv6)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var service = ipv6 ? "Tcpip6" : "Tcpip";
        var path = $@"SYSTEM\CurrentControlSet\Services\{service}\Parameters\Interfaces\{interfaceId}";
        using var key = Registry.LocalMachine.OpenSubKey(path);
        var staticNameServers = key?.GetValue("NameServer") as string;
        return string.IsNullOrWhiteSpace(staticNameServers);
    }
}
