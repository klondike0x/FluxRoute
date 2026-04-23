using System.Diagnostics;

namespace FluxRoute.Core.Services;

public sealed class ProcessHealthSnapshot
{
    public bool IsRunning { get; init; }
    public IReadOnlyList<int> ProcessIds { get; init; } = Array.Empty<int>();
    public int Count => ProcessIds.Count;

    public string Detail => IsRunning
        ? $"Найдено процессов: {Count} ({string.Join(", ", ProcessIds.Take(5))})"
        : "Процесс не найден";
}

public static class ProcessHealthChecker
{
    public static ProcessHealthSnapshot Snapshot(string processName = "winws")
    {
        try
        {
            var ids = new List<int>();

            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                            ids.Add(process.Id);
                    }
                    catch
                    {
                        // Процесс мог завершиться между получением списка и чтением свойств.
                    }
                }
            }

            ids.Sort();
            return new ProcessHealthSnapshot
            {
                IsRunning = ids.Count > 0,
                ProcessIds = ids
            };
        }
        catch
        {
            return new ProcessHealthSnapshot { IsRunning = false };
        }
    }

    public static async Task<ProcessHealthSnapshot> WaitForProcessAsync(
        string processName,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.Now + timeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var snapshot = Snapshot(processName);
            if (snapshot.IsRunning || DateTimeOffset.Now >= deadline)
                return snapshot;

            var delay = pollInterval <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(250)
                : pollInterval;

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }
}
