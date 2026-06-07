using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using FluxRoute.Core.Models;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    // ══════════════════════════════════════════════════════════════
    //  СУЩЕСТВУЮЩИЕ КОМАНДЫ (не трогаем)
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile is null) { Logs.Add("Стратегия не выбрана."); return; }
        Logs.Add($"Выбрана стратегия: {SelectedProfile.FileName}");
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(BuildDiagnosticsText());
            Logs.Add("Диагностика скопирована.");
        }
        catch (Exception ex) { Logs.Add($"Ошибка: {ex.Message}"); }
    }

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
        catch (Exception ex) { Logs.Add($"❌ Ошибка экспорта логов: {ex.Message}"); }
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

            var diagEntry = zip.CreateEntry("diagnostics.txt");
            using (var writer = new StreamWriter(diagEntry.Open(), Encoding.UTF8))
                writer.Write(BuildDiagnosticsText());

            var appLogEntry = zip.CreateEntry("app_log.txt");
            using (var writer = new StreamWriter(appLogEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"FluxRoute v{AppVersion} — Системный лог [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Logs) writer.WriteLine(line);
            }

            var orchEntry = zip.CreateEntry("orchestrator_log.txt");
            using (var writer = new StreamWriter(orchEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог оркестратора [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in OrchestratorLogs) writer.WriteLine(line);
            }

            var updateEntry = zip.CreateEntry("update_log.txt");
            using (var writer = new StreamWriter(updateEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог обновлений [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Updates.UpdateLogs) writer.WriteLine(line);
            }

            var serviceEntry = zip.CreateEntry("service_log.txt");
            using (var writer = new StreamWriter(serviceEntry.Open(), Encoding.UTF8))
            {
                writer.WriteLine($"Лог сервиса [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
                writer.WriteLine(new string('─', 60));
                foreach (var line in Service.ServiceLogs) writer.WriteLine(line);
            }

            var settingsPath = _settingsService.SettingsPath;
            if (File.Exists(settingsPath))
                zip.CreateEntryFromFile(settingsPath, "settings.json");

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluxRoute", "logs");
            if (Directory.Exists(logDir))
            {
                foreach (var file in Directory.EnumerateFiles(logDir, "*.log")
                             .OrderByDescending(File.GetLastWriteTime).Take(3))
                    zip.CreateEntryFromFile(file, $"serilog/{Path.GetFileName(file)}");
            }
            Logs.Add($"📦 Бандл сохранён: {dialog.FileName}");
        }
        catch (Exception ex) { Logs.Add($"❌ Ошибка экспорта бандла: {ex.Message}"); }
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
        UptimeText = "—";
        PidText = "—";
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

    private static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return asm.GetName().Version?.ToString(3) ?? "—";
    }

    private void LoadProfiles()
    {
        var currentFileName = SelectedProfile?.FileName;
        Profiles.Clear();
        if (!Directory.Exists(EngineDir)) { Logs.Add($"Папка engine не найдена: {EngineDir}"); SelectedProfile = null; return; }
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "service.bat", "service,.bat" };
        var aiEvolvedDir = Path.Combine(EngineDir, "ai-evolved");
        var bats = Directory.EnumerateFiles(EngineDir, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f => !excluded.Contains(Path.GetFileName(f)));
        if (Directory.Exists(aiEvolvedDir))
            bats = bats.Concat(Directory.EnumerateFiles(aiEvolvedDir, "*.bat", SearchOption.TopDirectoryOnly));
        var batList = bats.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var bat in batList)
            Profiles.Add(new ProfileItem { FileName = Path.GetFileName(bat), DisplayName = Path.GetFileNameWithoutExtension(bat), FullPath = bat });
        _suppressProfileWarning = true;
        try
        {
            if (currentFileName is not null)
                SelectedProfile = Profiles.FirstOrDefault(p => p.FileName == currentFileName) ?? Profiles.FirstOrDefault();
            else
                SelectedProfile ??= Profiles.FirstOrDefault();
        }
        finally { _suppressProfileWarning = false; }
        Logs.Add($"Стратегии загружены: {Profiles.Count} (.bat)");
        RebuildAiStrategyRows();
    }

    private string BuildDiagnosticsText() =>
        Diagnostics.BuildDiagnosticsText(AppVersion, StatusText, RunningScriptName, PidText, UptimeText, OrchestratorRunning);

    // ══════════════════════════════════════════════════════════════
    //  ▶ EXTENDED DIAGNOSTICS (кнопка на вкладке "Диагностика")
    //  Повторяет логику service.bat → пункт 10 (Run Diagnostics)
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool extendedDiagnosticsVisible;
    [ObservableProperty] private ObservableCollection<string> extendedDiagnosticsResults = new();
    [ObservableProperty] private bool isExtendedDiagnosticsRunning;

    // Ленивая инициализация провайдера кодовых страниц (CP866, CP437 и т.д.)
    private static volatile bool _codePagesRegistered;
    private static void EnsureCodePagesRegistered()
    {
        if (_codePagesRegistered) return;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // Игнорируем — если провайдер недоступен, будем использовать UTF-8
        }
        _codePagesRegistered = true;
    }

    [RelayCommand]
    private async Task RunExtendedDiagnostics()
    {
        if (IsExtendedDiagnosticsRunning) return;

        // Регистрируем CodePages перед первым использованием Encoding.GetEncoding(866)
        EnsureCodePagesRegistered();

        ExtendedDiagnosticsVisible = true;
        ExtendedDiagnosticsResults.Clear();
        IsExtendedDiagnosticsRunning = true;

        Diag("🔍 Запуск системной диагностики...");
        Diag($"⏰ {DateTime.Now:HH:mm:ss}");

        var isAdmin = false;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        Diag(isAdmin ? "✅ Права администратора: есть" : "⚠️ Права администратора: нет");
        Diag("");

        try
        {
            await Task.Run(() =>
            {
                // ── 1. Base Filtering Engine ────────────────────────────────
                Diag("▶ Base Filtering Engine");
                var bfeOut = RunHiddenCmd("sc query BFE");
                if (bfeOut.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    Diag("  ✅ Base Filtering Engine check passed");
                else if (bfeOut.Contains("1060") || bfeOut.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                    Diag("  ❌ [X] Base Filtering Engine не установлена в системе");
                else if (string.IsNullOrWhiteSpace(bfeOut))
                    Diag("  ❌ [X] sc query BFE вернул пустой ответ");
                else
                    Diag("  ❌ [X] Base Filtering Engine is not running. This service is required for zapret to work");
                Diag("");

                // ── 2. System Proxy ────────────────────────────────────────
                Diag("▶ System Proxy");
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false);
                    if (key is null)
                    {
                        Diag("  ⚠️ Не удалось открыть ключ реестра");
                    }
                    else
                    {
                        var proxyEnable = key.GetValue("ProxyEnable");
                        var isEnabled = proxyEnable is int i && i == 1
                                     || proxyEnable is string s && s == "0x1";
                        if (isEnabled)
                        {
                            var proxyServer = key.GetValue("ProxyServer")?.ToString() ?? "?";
                            Diag($"  ⚠️ [?] System proxy is enabled: {proxyServer}");
                            Diag("  ⚠️ Make sure it's valid or disable it if you don't use a proxy");
                        }
                        else Diag("  ✅ Proxy check passed");
                    }
                }
                catch (Exception ex) { Diag($"  ⚠️ Ошибка чтения реестра: {ex.Message}"); }
                Diag("");

                // ── 3. TCP Timestamps ──────────────────────────────────────
                Diag("▶ TCP Timestamps");
                var tcpOut = RunHiddenCmd("netsh interface tcp show global");
                var tcpEnabled = tcpOut.Split('\n', '\r')
                    .Any(l => (l.Contains("timestamps", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("Метки времени", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("Временные метки", StringComparison.OrdinalIgnoreCase)) &&
                              (l.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("включен", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("включены", StringComparison.OrdinalIgnoreCase)));
                if (tcpEnabled)
                {
                    Diag("  ✅ TCP timestamps check passed");
                }
                else
                {
                    Diag("  ⚠️ [?] TCP timestamps are disabled. Enabling timestamps...");
                    if (!isAdmin)
                    {
                        Diag("  ❌ [X] Не удалось включить: требуются права администратора");
                    }
                    else
                    {
                        RunHiddenCmd("netsh interface tcp set global timestamps=enabled");
                        var check = RunHiddenCmd("netsh interface tcp show global");
                        var nowOn = check.Split('\n', '\r')
                            .Any(l => (l.Contains("timestamps", StringComparison.OrdinalIgnoreCase) ||
                                       l.Contains("Метки времени", StringComparison.OrdinalIgnoreCase) ||
                                       l.Contains("Временные метки", StringComparison.OrdinalIgnoreCase)) &&
                                      (l.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
                                       l.Contains("включен", StringComparison.OrdinalIgnoreCase) ||
                                       l.Contains("включены", StringComparison.OrdinalIgnoreCase)));
                        Diag(nowOn ? "  ✅ TCP timestamps successfully enabled" : "  ❌ [X] Failed to enable TCP timestamps");
                    }
                }
                Diag("");

                // ── 4. AdGuard process ─────────────────────────────────────
                Diag("▶ AdGuard");
                var adguardOut = RunHiddenCmd("tasklist /FI \"IMAGENAME eq AdguardSvc.exe\"");
                if (adguardOut.Contains("AdguardSvc.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Diag("  ❌ [X] Adguard process found. Adguard may cause problems with Discord");
                    Diag("  ❌ https://github.com/Flowseal/zapret-discord-youtube/issues/417");
                }
                else Diag("  ✅ Adguard check passed");
                Diag("");

                // ── 5-8. Службы (один раз получаем список) ─────────────────
                var allServices = RunHiddenCmd("sc query state= all", 8000);

                Diag("▶ Killer Network Services");
                Diag(allServices.Split('\n', '\r').Any(l => l.Contains("Killer", StringComparison.OrdinalIgnoreCase))
                    ? "  ❌ [X] Killer services found. Killer conflicts with zapret"
                    : "  ✅ Killer check passed");
                Diag("");

                Diag("▶ Intel Connectivity Network Service");
                var intelFound = allServices.Split('\n', '\r')
                    .Any(l => l.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                              l.Contains("Connectivity", StringComparison.OrdinalIgnoreCase) &&
                              l.Contains("Network", StringComparison.OrdinalIgnoreCase));
                Diag(intelFound
                    ? "  ❌ [X] Intel Connectivity Network Service found. It conflicts with zapret"
                    : "  ✅ Intel Connectivity check passed");
                Diag("");

                Diag("▶ Check Point");
                var cp1 = allServices.Split('\n', '\r').Any(l => l.Contains("TracSrvWrapper", StringComparison.OrdinalIgnoreCase));
                var cp2 = allServices.Split('\n', '\r').Any(l => l.Contains("EPWD", StringComparison.OrdinalIgnoreCase));
                Diag((cp1 || cp2)
                    ? "  ❌ [X] Check Point services found. Check Point conflicts with zapret"
                    : "  ✅ Check Point check passed");
                Diag("");

                Diag("▶ SmartByte");
                var smartByte = allServices.Split('\n', '\r').Any(l => l.Contains("SmartByte", StringComparison.OrdinalIgnoreCase));
                Diag(smartByte
                    ? "  ❌ [X] SmartByte services found. SmartByte conflicts with zapret"
                    : "  ✅ SmartByte check passed");
                Diag("");

                // ── 9. WinDivert file ──────────────────────────────────────
                Diag("▶ WinDivert64.sys");
                var binPath = Path.Combine(EngineDir, "bin");
                var hasSys = Directory.Exists(binPath) && Directory.GetFiles(binPath, "*.sys").Length > 0;
                Diag(!hasSys ? "  ❌ WinDivert64.sys file NOT found." : "  ✅ WinDivert driver file found");
                Diag("");

                // ── 10. VPN services ───────────────────────────────────────
                Diag("▶ VPN services");
                var vpnLines = new List<string>();
                foreach (var line in allServices.Split('\n', '\r'))
                {
                    var trimmed = line.Trim();
                    if ((trimmed.StartsWith("SERVICE_NAME", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("DISPLAY_NAME", StringComparison.OrdinalIgnoreCase)) &&
                        trimmed.Contains(':'))
                    {
                        var serviceName = trimmed.Split(':', 2).LastOrDefault()?.Trim() ?? "";
                        if (serviceName.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                            !vpnLines.Contains(serviceName, StringComparer.OrdinalIgnoreCase))
                        {
                            vpnLines.Add(serviceName);
                        }
                    }
                }
                if (vpnLines.Count > 0)
                {
                    Diag($"  ⚠️ [?] VPN services found: {string.Join(", ", vpnLines)}. Some VPNs can conflict with zapret");
                    Diag("  ⚠️ Make sure that all VPNs are disabled");
                }
                else Diag("  ✅ VPN check passed");
                Diag("");

                // ── 11. Hosts file ─────────────────────────────────────────
                Diag("▶ Hosts file");
                try
                {
                    var hostsFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "drivers", "etc", "hosts");
                    if (File.Exists(hostsFile))
                    {
                        var hosts = File.ReadAllText(hostsFile);
                        var hasYt = hosts.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                                 || hosts.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
                        Diag(hasYt
                            ? "  ⚠️ [?] Your hosts file contains entries for youtube.com or youtu.be"
                            : "  ✅ Hosts file is clean");
                    }
                    else Diag("  ⚠️ Hosts file not found");
                }
                catch (Exception ex) { Diag($"  ⚠️ Ошибка чтения hosts: {ex.Message}"); }
                Diag("");

                // ── 12. Конфликтующие bypass-сервисы ───────────────────────
                Diag("▶ Conflicting bypass services");
                var conflicts = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" };
                var foundConflicts = new List<string>();
                foreach (var svc in conflicts)
                {
                    var o = RunHiddenCmd($"sc query {svc}");
                    if (o.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase) ||
                        o.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                        o.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                        foundConflicts.Add(svc);
                }
                if (foundConflicts.Count > 0)
                    Diag($"  ❌ [X] Conflicting bypass services found: {string.Join(", ", foundConflicts)}");
                else Diag("  ✅ Conflicting bypass services not found");
            });

            Diag("");
            Diag("✅ Диагностика завершена");
        }
        catch (Exception ex)
        {
            Diag($"❌ Ошибка диагностики: {ex.Message}");
        }
        finally
        {
            IsExtendedDiagnosticsRunning = false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  ▶ КОМАНДЫ УПРАВЛЕНИЯ РЕЗУЛЬТАТАМИ ДИАГНОСТИКИ
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void CopyExtendedDiagnostics()
    {
        try
        {
            if (ExtendedDiagnosticsResults.Count == 0)
            {
                Logs.Add("⚠️ Результаты диагностики пусты — нечего копировать");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"FluxRoute v{AppVersion} — Диагностика [{DateTime.Now:dd.MM.yyyy HH:mm:ss}]");
            sb.AppendLine($"Admin: {(Diagnostics.IsAdmin ? "Yes" : "No")}");
            sb.AppendLine($"Engine: {EngineText}");
            sb.AppendLine($"winws.exe: {WinwsText}");
            sb.AppendLine($"WinDivert.dll: {WinDivertDllText}");
            sb.AppendLine($"WinDivert.sys: {WinDivertDriverText}");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine();

            foreach (var line in ExtendedDiagnosticsResults)
                sb.AppendLine(line);

            Clipboard.SetText(sb.ToString());
            Logs.Add("📋 Результаты диагностики скопированы в буфер обмена");
        }
        catch (Exception ex)
        {
            Logs.Add($"❌ Ошибка копирования: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearExtendedDiagnostics()
    {
        ExtendedDiagnosticsResults.Clear();
        ExtendedDiagnosticsVisible = false;
        Logs.Add("🗑 Результаты диагностики очищены");
    }

    // ══════════════════════════════════════════════════════════════
    //  ▶ ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ══════════════════════════════════════════════════════════════
    private void Diag(string message)
    {
        Application.Current?.Dispatcher.Invoke(() => ExtendedDiagnosticsResults.Add(message));
    }

    /// <summary>
    /// Запускает команду скрытно с правами администратора (наследуются от родителя).
    /// LoadUserProfile = true критичен для наследования elevated-токена через UseShellExecute = false.
    /// Кодировка 866 — стандартная OEM-кодировка русской Windows (совпадает с chcp 866 в BAT).
    /// </summary>
    /// <summary>
    /// Запускает команду скрытно с правами администратора (наследуются от родителя).
    /// Повторяет поведение service.bat: chcp 437 перед каждой командой,
    /// чтобы системные утилиты (netsh, sc, tasklist) выводили на английском.
    /// </summary>
    private static string RunHiddenCmd(string cmd, int timeoutMs = 5000)
    {
        EnsureCodePagesRegistered();

        try
        {
            // CP437 — американская OEM-кодировка. В ней netsh/sc выводят на английском.
            Encoding oemEncoding;
            try
            {
                oemEncoding = Encoding.GetEncoding(437);
            }
            catch
            {
                oemEncoding = Encoding.UTF8;
            }

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    // chcp 437 >nul — переключает кодировку консоли на английскую OEM,
                    // >nul скрывает "Active code page: 437",
                    // & — затем выполняет нужную команду.
                    // Это точно повторяет поведение service.bat.
                    Arguments = $"/c chcp 437 >nul & {cmd}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    LoadUserProfile = true,
                    StandardOutputEncoding = oemEncoding,
                    StandardErrorEncoding = oemEncoding,
                }
            };
            p.Start();

            var outputTask = p.StandardOutput.ReadToEndAsync();
            var errorTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return "";
            }

            var output = outputTask.Result;
            var error = errorTask.Result;

            if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error))
                return $"[STDERR] {error.Trim()}";

            return output;
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }
    }
}