// ═══ v1.7.0: НОВОЕ — реализация исключений Защитника Windows ═══
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public class AntivirusExclusionService : IAntivirusExclusionService
{
    private readonly ILogger<AntivirusExclusionService> _logger;

    public AntivirusExclusionService(ILogger<AntivirusExclusionService> logger) => _logger = logger;

    public async Task<AntivirusExclusionResult> AddExclusionAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return new() { Path = folderPath ?? "", Success = false, Message = "Путь не указан." };

        folderPath = folderPath.TrimEnd('\\', '/');
        if (!Directory.Exists(folderPath))
            return new() { Path = folderPath, Success = false, Message = $"Папка не найдена: {folderPath}" };

        if (!IsAdmin())
        {
            _logger.LogWarning("Требуются права администратора для {Path}", folderPath);
            return new() { Path = folderPath, Success = false, RequiresElevation = true, Message = "Требуются права администратора." };
        }

        if (await IsExcludedAsync(folderPath, ct))
            return new() { Path = folderPath, Success = true, Message = "Папка уже в исключениях Защитника Windows." };

        try
        {
            var args = $"-NoProfile -NonInteractive -Command \"Add-MpPreference -ExclusionPath '{folderPath.Replace("'", "''")}'\"";
            var psi = new ProcessStartInfo { FileName = "powershell.exe", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return new() { Path = folderPath, Success = false, Message = "Не удалось запустить PowerShell." };
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            if (p.ExitCode == 0) return new() { Path = folderPath, Success = true, Message = "Папка добавлена в исключения Защитника Windows." };
            return new() { Path = folderPath, Success = false, Message = $"Ошибка PowerShell (код {p.ExitCode}): {err.Trim()}" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка исключения Защитника: {Path}", folderPath);
            return new() { Path = folderPath, Success = false, Message = $"Ошибка: {ex.Message}" };
        }
    }

    public async Task<bool> IsExcludedAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;
        folderPath = folderPath.TrimEnd('\\', '/');
        try
        {
            var args = $"-NoProfile -NonInteractive -Command \"(Get-MpPreference).ExclusionPath -contains '{folderPath.Replace("'", "''")}'\"";
            var psi = new ProcessStartInfo { FileName = "powershell.exe", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            return output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsAdmin() { using var i = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(i).IsInRole(WindowsBuiltInRole.Administrator); }
}
