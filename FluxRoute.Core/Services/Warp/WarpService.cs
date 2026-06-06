using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluxRoute.Core.Services.Warp;

public class WarpConfig
{
    public string PrivateKey { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string AddressV4 { get; set; } = "";
    public string AddressV6 { get; set; } = "";
    public string Endpoint { get; set; } = "engage.cloudflareclient.com:2408";
    public string Reserved { get; set; } = ""; // 3 bytes base64
}

public class WarpService
{
    private readonly HttpClient _httpClient;

    public WarpService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WarpConfig> RegisterAsync()
    {
        // In a real implementation, we would use a library like NSec.Cryptography or Sodium.Core for Curve25519.
        // For now, we'll simulate the generation of keys and registration.

        var privateKeyBytes = new byte[32];
        new Random().NextBytes(privateKeyBytes);
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        var publicKeyBytes = new byte[32];
        new Random().NextBytes(publicKeyBytes);
        var publicKey = Convert.ToBase64String(publicKeyBytes);

        var config = new WarpConfig
        {
            PrivateKey = privateKey,
            PublicKey = publicKey,
            AddressV4 = "172.16.0.2/32",
            AddressV6 = "fd01:5ca1:ab1e:8273:c71:153e:d632:155e/128",
            Reserved = "AAAA"
        };

        // Simulate network delay
        await Task.Delay(1500);

        return config;
    }

    public string GenerateWireGuardConfig(WarpConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {config.PrivateKey}");
        sb.AppendLine($"Address = {config.AddressV4}, {config.AddressV6}");
        sb.AppendLine("DNS = 1.1.1.1");
        sb.AppendLine("");
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {config.PublicKey}");
        sb.AppendLine($"Endpoint = {config.Endpoint}");
        sb.AppendLine("AllowedIPs = 0.0.0.0/0, ::/0");

        return sb.ToString();
    }
}
