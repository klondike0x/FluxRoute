using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Clipboard = System.Windows.Clipboard;

using FluxRoute.Core.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    [RelayCommand] private void ApplyProfile() { if (SelectedProfile is null) { Logs.Add("Профиль не выбран."); return; } Logs.Add($"Выбран профиль: {SelectedProfile.FileName}"); }
    [RelayCommand] private void CopyDiagnostics() { try { Clipboard.SetText(BuildDiagnosticsText()); Logs.Add("Диагностика скопирована."); } catch (Exception ex) { Logs.Add($"Ошибка: {ex.Message}"); } }

    [RelayCommand]
    private void ExportLogs()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Текстовый файл (*.txt)|*.txt",
                FileName = $"FluxRoute_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt",
                Title = "Экспорт логов"
            };

            if (dialog.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine($"FluxRoute v{AppVersion} — Лог от {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine();

            sb.AppendLine("── Системный лог ──");
            foreach (var line in Logs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Лог оркестратора ──");
            foreach (var line in OrchestratorLogs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Лог обновлений ──");
            foreach (var line in UpdateLogs) sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("── Диагностика ──");
            sb.Append(BuildDiagnosticsText());

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            Logs.Add($"📄 Логи экспортированы: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка экспорта логов: {ex.Message}");
        }
    }

    private void RefreshDiagnostics()
    {
        IsAdmin = CheckIsAdmin();
        EngineOk = Directory.Exists(EngineDir);
        WinwsOk = File.Exists(WinwsPath);
        WinDivertDllOk = File.Exists(WinDivertDllPath);
        WinDivertDriverOk = File.Exists(WinDivertSys64Path) || File.Exists(WinDivertSysPath);
        
        // Расширенная проверка конфигурации (как в Zapret-Hub)
        ValidateConfiguration();
    }

    private void ValidateConfiguration()
    {
        try
        {
            var results = ConfigValidator.ValidateAll(
                engineDir: EngineDir,
                winwsPath: WinwsPath,
                winDivertDllPath: WinDivertDllPath,
                winDivertSysPath: WinDivertSysPath,
                winDivertSys64Path: WinDivertSys64Path,
                getProfiles: () => Profiles);

            var hasErrors = results.Any(r => r.Status == "error");
            var hasWarnings = results.Any(r => r.Status == "warning");

            if (hasErrors)
            {
                Logs.Add("❌ Проверка конфигурации обнаружила критические ошибки:");
                foreach (var result in results.Where(r => r.Status == "error"))
                    Logs.Add($"   • {result.Name}: {result.Message}");
            }
            else if (hasWarnings)
            {
                Logs.Add("⚠️ Проверка конфигурации обнаружила предупреждения:");
                foreach (var result in results.Where(r => r.Status == "warning"))
                    Logs.Add($"   • {result.Name}: {result.Message}");
            }
            else
            {
                Logs.Add("✅ Проверка конфигурации пройдена успешно");
            }
        }
        catch (Exception ex)
        {
            Logs.Add($"⚠️ Ошибка при проверке конфигурации: {ex.Message}");
        }
    }

    private void UpdateRuntimeInfo()
    {
        if (_runningProcess is { HasExited: false } && _runStartedAt is not null)
        {
            var ts = DateTimeOffset.Now - _runStartedAt.Value;
            UptimeText = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            PidText = _runningProcess.Id.ToString();
            IsRunning = true;
            return;
        }
        UptimeText = "—"; PidText = "—";
        if (_runningProcess is { HasExited: true })
        {
            _runningProcess.Dispose();
            _runningProcess = null;
            StatusText = "Не запущено";
            CurrentStrategy = "—";
            RunningScriptName = "—";
            IsRunning = false;
        }
    }

    private static bool CheckIsAdmin() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
    private static string GetAppVersion() { var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(); return asm.GetName().Version?.ToString(3) ?? "—"; }

    private void LoadProfiles()
    {
        var currentFileName = SelectedProfile?.FileName;

        Profiles.Clear();
        if (!Directory.Exists(EngineDir)) { Logs.Add($"Папка engine не найдена: {EngineDir}"); SelectedProfile = null; return; }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat", "service,.bat" };
        var bats = Directory.EnumerateFiles(EngineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !excluded.Contains(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var bat in bats)
            Profiles.Add(new ProfileItem { FileName = Path.GetFileName(bat), DisplayName = Path.GetFileNameWithoutExtension(bat), FullPath = bat });

        if (currentFileName is not null)
            SelectedProfile = Profiles.FirstOrDefault(p => p.FileName == currentFileName)
                              ?? Profiles.FirstOrDefault();
        else
            SelectedProfile ??= Profiles.FirstOrDefault();

        Logs.Add($"Профили загружены: {Profiles.Count} (.bat)");
    }

    private string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("FluxRoute Desktop"); sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Admin: {(IsAdmin ? "Yes" : "No")}"); sb.AppendLine($"Engine: {EngineText} ({EngineDir})");
        sb.AppendLine($"winws.exe: {WinwsText}"); sb.AppendLine($"WinDivert.dll: {WinDivertDllText}");
        sb.AppendLine($"WinDivert.sys: {WinDivertDriverText}"); sb.AppendLine($"Status: {StatusText}");
        sb.AppendLine($"Running BAT: {RunningScriptName}"); sb.AppendLine($"PID: {PidText}");
        sb.AppendLine($"Uptime: {UptimeText}"); sb.AppendLine($"Orchestrator: {(OrchestratorRunning ? "Running" : "Stopped")}");
        
        // Добавляем расширенную проверку конфигурации
        sb.AppendLine();
        sb.AppendLine("═══ Проверка конфигурации ═══");
        try
        {
            var results = ConfigValidator.ValidateAll(
                engineDir: EngineDir,
                winwsPath: WinwsPath,
                winDivertDllPath: WinDivertDllPath,
                winDivertSysPath: WinDivertSysPath,
                winDivertSys64Path: WinDivertSys64Path,
                getProfiles: () => Profiles);
            
            sb.AppendLine(ConfigValidator.FormatResults(results));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Ошибка проверки: {ex.Message}");
        }
        
        return sb.ToString();
    }
}
