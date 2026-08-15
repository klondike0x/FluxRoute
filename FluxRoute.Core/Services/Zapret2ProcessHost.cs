using System.Diagnostics;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

/// <summary>Ограниченный адаптер процесса winws2 без зависимости UI от System.Diagnostics.</summary>
public sealed class Zapret2ProcessHost : IZapret2ProcessHost
{
    private readonly ILogger<Zapret2ProcessHost> _logger;
    private readonly HashSet<int> _ownedProcessIds = [];
    private readonly object _ownershipGate = new();

    public Zapret2ProcessHost(ILogger<Zapret2ProcessHost> logger)
    {
        _logger = logger;
    }

    public Zapret2ProcessSnapshot Snapshot()
    {
        try
        {
            var ids = Process.GetProcessesByName("winws2")
                .Select(process =>
                {
                    try { return process.HasExited ? 0 : process.Id; }
                    catch { return 0; }
                    finally { process.Dispose(); }
                })
                .Where(id => id > 0)
                .Distinct()
                .Order()
                .ToArray();

            return new Zapret2ProcessSnapshot(
                ids.Length > 0,
                ids,
                ids.Length > 0
                    ? $"Найдено процессов winws2: {ids.Length}."
                    : "Процесс winws2 не найден.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить состояние процесса winws2.");
            return new Zapret2ProcessSnapshot(false, Array.Empty<int>(), "Не удалось проверить winws2.");
        }
    }

    public async Task<Zapret2OperationResult> StartAsync(
        Zapret2StartRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
            return new(false, $"winws2.exe не найден: {request.ExecutablePath}");

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory))
            return new(false, $"Рабочая папка Zapret2 не найдена: {request.WorkingDirectory}");

        if (Snapshot().IsRunning)
            return new(false, "Процесс winws2 уже запущен.");

        try
        {
            ct.ThrowIfCancellationRequested();
            var process = await Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = request.ExecutablePath,
                    WorkingDirectory = request.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                foreach (var argument in request.Arguments)
                    startInfo.ArgumentList.Add(argument);

                return Process.Start(startInfo);
            }, ct).ConfigureAwait(false);

            if (process is null)
                return new(false, "Не удалось создать процесс winws2.");

            var processId = process.Id;
            process.Dispose();
            lock (_ownershipGate)
                _ownedProcessIds.Add(processId);
            _logger.LogInformation("Запущен winws2, PID: {ProcessId}, профиль: {Profile}", processId, request.ActiveProfile);
            return new(true, $"winws2 запущен, PID: {processId}.", processId);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Запуск winws2 отменён.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка запуска winws2.");
            return new(false, $"Ошибка запуска winws2: {ex.Message}");
        }
    }

    public async Task<Zapret2OperationResult> StopAsync(CancellationToken ct = default)
    {
        try
        {
            int[] ownedProcessIds;
            lock (_ownershipGate)
                ownedProcessIds = _ownedProcessIds.ToArray();

            var stopped = 0;

            foreach (var processId in ownedProcessIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(ct).ConfigureAwait(false);
                        stopped++;
                    }
                }
                catch (ArgumentException)
                {
                    // Процесс уже завершился.
                }
                catch (InvalidOperationException)
                {
                    // Процесс мог завершиться между проверкой и остановкой.
                }
            }

            lock (_ownershipGate)
                _ownedProcessIds.Clear();

            _logger.LogInformation("Остановлено процессов winws2: {Count}.", stopped);
            return new(true, stopped == 0 ? "Активный winws2 не найден." : $"Остановлено процессов winws2: {stopped}.");
        }
        catch (OperationCanceledException)
        {
            return new(false, "Остановка winws2 отменена.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка остановки winws2.");
            return new(false, $"Ошибка остановки winws2: {ex.Message}");
        }
    }
}
