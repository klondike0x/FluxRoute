using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class NetworkFingerprintProvider
{
    public NetworkFingerprint Capture()
    {
        var parts = new List<string>();
        var labelParts = new List<string>();
        string transport = "";
        string gatewayIp = "";
        var dnsList = new SortedSet<string>(StringComparer.Ordinal);
        var subnets = new SortedSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(u => u.Address.ToString())
                    .OrderBy(x => x)
                    .ToList();

                if (ipv4.Count == 0)
                    continue;

                transport = nic.NetworkInterfaceType.ToString();
                parts.Add($"nic:{nic.Id}|{nic.NetworkInterfaceType}|{string.Join(",", ipv4)}");

                foreach (var g in ipProps.GatewayAddresses)
                {
                    if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        gatewayIp = g.Address.ToString();
                        parts.Add($"gw:{gatewayIp}");
                        labelParts.Add($"{nic.NetworkInterfaceType}/{gatewayIp}");
                        break;
                    }
                }

                foreach (var uni in ipProps.UnicastAddresses.Where(u =>
                             u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    var pl = uni.PrefixLength;
                    if (pl <= 0 || pl > 32)
                        pl = 24;
                    subnets.Add($"{uni.Address}/{pl}");
                }

                foreach (var dns in ipProps.DnsAddresses)
                {
                    if (dns.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        dnsList.Add(dns.ToString());
                }
            }

        }
        catch
        {
        }

        foreach (var d in dnsList)
            parts.Add($"dns:{d}");
        foreach (var s in subnets)
            parts.Add($"sn:{s}");

        parts.Sort(StringComparer.Ordinal);
        var canonical = string.Join('\n', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hashHex = Convert.ToHexStringLower(hash);

        var label = labelParts.Count > 0
            ? string.Join(" · ", labelParts.Distinct())
            : (transport.Length > 0 ? transport : "Сеть");

        return new NetworkFingerprint
        {
            Hash = hashHex,
            Label = label,
            Transport = transport,
            GatewayIp = gatewayIp,
            DnsServers = dnsList.ToList(),
            LocalSubnet = string.Join(",", subnets),
            CapturedAt = DateTimeOffset.UtcNow,
        };
    }
}
