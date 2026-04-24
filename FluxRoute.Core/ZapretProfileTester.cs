using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace FluxRoute.Core
{
    public class SystemDiagnostics
    {
        public bool IsWinDivertRunning { get; set; }
        public bool IsPortAvailable { get; set; }
        public bool HasInternetAccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static async Task<SystemDiagnostics> RunAsync()
        {
            var diag = new SystemDiagnostics();
            diag.IsWinDivertRunning = Process.GetProcessesByName("winws").Length > 0;
            diag.IsPortAvailable = !IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(e => e.Port == 9888);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                diag.HasInternetAccess = reply.Status == IPStatus.Success;
            }
            catch { diag.HasInternetAccess = false; }
            if (!diag.IsWinDivertRunning) diag.ErrorMessage += "WinDivert not running. ";
            if (!diag.HasInternetAccess) diag.ErrorMessage += "No internet. ";
            return diag;
        }
    }

    public class ProfileTestResult
    {
        public string ProfileName { get; set; }
        public long LatencyMs { get; set; }
        public double Stability { get; set; }
        public double ThroughputMbps { get; set; }
        public double Score { get; set; }
    }

    public class ZapretProfileTester
    {
        public double WeightLatency { get; set; } = 0.4;
        public double WeightStability { get; set; } = 0.3;
        public double WeightThroughput { get; set; } = 0.3;

        public async Task<ProfileTestResult> TestProfileAsync(string name, string url)
        {
            var result = new ProfileTestResult { ProfileName = name };
            var diag = await SystemDiagnostics.RunAsync();
            if (!string.IsNullOrEmpty(diag.ErrorMessage))
                return result;

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var sw = Stopwatch.StartNew();
            var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            result.LatencyMs = sw.ElapsedMilliseconds;
            if (resp.IsSuccessStatusCode)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                double secs = sw.Elapsed.TotalSeconds;
                result.ThroughputMbps = (bytes.Length * 8) / (secs * 1_000_000);
            }

            int ok = 0;
            for (int i = 0; i < 5; i++)
            {
                try { var r = await http.GetAsync(url); if (r.IsSuccessStatusCode) ok++; }
                catch { }
                await Task.Delay(200);
            }
            result.Stability = ok / 5.0;
            result.Score = (Math.Min(100, 1000.0 / result.LatencyMs) * WeightLatency) +
                           (result.Stability * 100 * WeightStability) +
                           (Math.Min(100, result.ThroughputMbps * 10) * WeightThroughput);
            return result;
        }
    }
}
