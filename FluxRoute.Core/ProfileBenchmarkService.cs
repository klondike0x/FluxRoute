using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluxRoute.Core
{
    public class BenchmarkResult
    {
        public string ProfileName { get; set; } = string.Empty; = string.Empty; = string.Empty;
        public long LatencyMs { get; set; }
        public double StabilityRate { get; set; }
        public double ThroughputMbps { get; set; }
        public double Score { get; set; }
    }

    public class ProfileBenchmarkService
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        public event Action<string, double, int> ProgressChanged;

        public async Task<List<BenchmarkResult>> BenchmarkProfilesAsync(
            IEnumerable<(string name, string url)> profiles,
            CancellationToken cancel = default)
        {
            var results = new List<BenchmarkResult>();
            var profileList = new List<(string, string)>(profiles);
            int total = profileList.Count;
            var overallSw = Stopwatch.StartNew();

            for (int i = 0; i < total; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var (name, url) = profileList[i];
                double progress = (double)i / total * 100;
                double elapsedSec = overallSw.Elapsed.TotalSeconds;
                double remainingSec = i > 0 ? (elapsedSec / i) * (total - i) : total * 3;
                ProgressChanged?.Invoke($"⏳ {name}", progress, (int)remainingSec);

                var result = new BenchmarkResult { ProfileName = name };
                try
                {
                    var sw = Stopwatch.StartNew();
                    var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
                    result.LatencyMs = sw.ElapsedMilliseconds;
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        result.ThroughputMbps = (bytes.Length * 8) / (sw.Elapsed.TotalSeconds * 1_000_000);
                    }

                    int ok = 0;
                    for (int j = 0; j < 5; j++)
                    {
                        if (cancel.IsCancellationRequested) break;
                        try
                        {
                            var r = await _http.GetAsync(url, cancellationToken: cancel);
                            if (r.IsSuccessStatusCode) ok++;
                        }
                        catch { }
                        await Task.Delay(200, cancel);
                    }
                    result.StabilityRate = ok / 5.0;
                    result.Score = (Math.Min(100, 1000.0 / Math.Max(1, result.LatencyMs)) * 0.4) +
                                   (result.StabilityRate * 100 * 0.3) +
                                   (Math.Min(100, result.ThroughputMbps * 10) * 0.3);
                }
                catch
                {
                    result.Score = 0;
                }
                results.Add(result);
            }

            ProgressChanged?.Invoke($"✅ Завершено за {overallSw.Elapsed.TotalSeconds:F0} сек", 100, 0);
            return results;
        }
    }
}



