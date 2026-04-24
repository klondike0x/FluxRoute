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
}
