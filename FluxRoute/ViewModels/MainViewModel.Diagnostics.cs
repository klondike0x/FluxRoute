using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Clipboard = System.Windows.Clipboard;

using FluxRoute.Core.Models;

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
            foreach (var line in Updates.UpdateLogs) sb.AppendLine(line);
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

    [RelayCommand]
    private void ExportDiagnosticBundle()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ZIP-архив (*.zip)|*.zip",
                FileName = $"FluxRoute_bundle_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip",
                Title = "Сохранить диагностический бандл"
            };

            if (dialog.ShowDialog() != true) return;

            using var zip = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);

            // ── diagnostics.txt ────────────────────────────────────────────────
            var diagEntry = zip.CreateEntry("diagnostics.txt");
            using (var writer = new StreamWriter(diagEntry.Open(), Encoding.UTF8))
                writer.Write(BuildDiagnosticsText());

            // ── app_log.txt ────────────────────────────────────────────────────
            var appLogEntry = zip.CreateEntry("app_log.txt");
            using (var writer = new StreamWriter(appLogEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"FluxRoute v{AppVersion} — Системный лог [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Logs) writer.WriteLine(line);
            }

            // ── orchestrator_log.txt ───────────────────────────────────────────
            var orchEntry = zip.CreateEntry("orchestrator_log.txt");
            using (var writer = new StreamWriter(orchEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог оркестратора [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in OrchestratorLogs) writer.WriteLine(line);
            }

            // ── update_log.txt ─────────────────────────────────────────────────
            var updateEntry = zip.CreateEntry("update_log.txt");
            using (var writer = new StreamWriter(updateEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог обновлений [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Updates.UpdateLogs) writer.WriteLine(line);
            }

            // ── service_log.txt ────────────────────────────────────────────────
            var serviceEntry = zip.CreateEntry("service_log.txt");
            using (var writer = new StreamWriter(serviceEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог сервиса [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Service.ServiceLogs) writer.WriteLine(line);
            }

            // ── settings.json (если существует) ───────────────────────────────
            var settingsPath = _settingsService.SettingsPath;
            if (File.Exists(settingsPath))
                zip.CreateEntryFromFile(settingsPath, "settings.json");

            // ── Serilog лог-файлы из %LocalAppData%\FluxRoute\logs ─────────────
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluxRoute", "logs");

            if (Directory.Exists(logDir))
            {
                foreach (var file in Directory.EnumerateFiles(logDir, "*.log")
                             .OrderByDescending(File.GetLastWriteTime)
                             .Take(3))
                {
                    zip.CreateEntryFromFile(file, $"serilog/{Path.GetFileName(file)}");
                }
            }

            Logs.Add($"📦 Бандл сохранён: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка экспорта бандла: {ex.Message}");
        }
    }

    private void RefreshDiagnostics() => Diagnostics.Refresh();

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

    private string BuildDiagnosticsText() =>
        Diagnostics.BuildDiagnosticsText(AppVersion, StatusText, RunningScriptName, PidText, UptimeText, OrchestratorRunning);
}
