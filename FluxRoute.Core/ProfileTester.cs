using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace FluxRoute.Core
{
    public class ProfileMetrics
    {
        public string ProfileName { get; set; } = string.Empty;
        public long LatencyMs { get; set; }
        public double SpeedMbps { get; set; }
        public double StabilityRate { get; set; }
        public bool IsAccessible { get; set; }
        public double Score { get; set; }
    }

    public class ProfileTester
    {
        private readonly HttpClient _httpClient;
        public double WeightLatency { get; set; } = 0.4;
        public double WeightSpeed { get; set; } = 0.4;
        public double WeightStability { get; set; } = 0.2;

        public ProfileTester()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<ProfileMetrics> TestProfileAsync(string profileName, string testUrl)
        {
            var metrics = new ProfileMetrics { ProfileName = profileName };
            try
            {
                var sw = Stopwatch.StartNew();
                var response = await _httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();
                metrics.LatencyMs = sw.ElapsedMilliseconds;
                metrics.IsAccessible = response.IsSuccessStatusCode;

                if (metrics.IsAccessible)
                {
                    var speedSw = Stopwatch.StartNew();
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    speedSw.Stop();
                    double seconds = speedSw.Elapsed.TotalSeconds;
                    double megabits = (bytes.Length * 8) / 1_000_000.0;
                    metrics.SpeedMbps = seconds > 0 ? megabits / seconds : 0;

                    int successCount = 0;
                    int totalAttempts = 3;
                    for (int i = 0; i < totalAttempts; i++)
                    {
                        try
                        {
                            var check = await _httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead);
                            if (check.IsSuccessStatusCode) successCount++;
                        }
                        catch { }
                        await Task.Delay(200);
                    }
                    metrics.StabilityRate = (double)successCount / totalAttempts;
                }
                metrics.Score = CalculateScore(metrics);
            }
            catch
            {
                metrics.IsAccessible = false;
                metrics.Score = 0;
            }
            return metrics;
        }

        private double CalculateScore(ProfileMetrics m)
        {
            if (!m.IsAccessible) return 0;
            double latencyScore = m.LatencyMs < 50 ? 100 : m.LatencyMs < 150 ? 70 : m.LatencyMs < 300 ? 40 : 10;
            double speedScore = m.SpeedMbps > 80 ? 100 : m.SpeedMbps > 40 ? 70 : m.SpeedMbps > 15 ? 40 : 10;
            double stabilityScore = m.StabilityRate * 100;
            return (latencyScore * WeightLatency) + (speedScore * WeightSpeed) + (stabilityScore * WeightStability);
        }
    }
}
