using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;

using FluxRoute.Views;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    private static readonly Dictionary<string, string> _protocolToFile = new()
    {
        ["TCP и UDP"] = "all",
        ["TCP"] = "tcp",
        ["UDP"] = "udp"
    };

    private static readonly Dictionary<string, string> _fileToProtocol = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = "TCP и UDP",
        ["tcp"] = "TCP",
        ["udp"] = "UDP"
    };

    private string ProtocolToFileValue(string protocol) =>
        _protocolToFile.TryGetValue(protocol, out var v) ? v : "udp";

    private string FileValueToProtocol(string fileValue) =>
        _fileToProtocol.TryGetValue(fileValue, out var v) ? v : "UDP";

    private void AddServiceLog(string message)
    {
        ServiceLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (ServiceLogs.Count > 50)
            ServiceLogs.RemoveAt(0);
    }

    private void RefreshServiceStatus()
    {
        // Game Filter
        if (File.Exists(GameFilterFlagPath))
        {
            GameFilterEnabled = true;
            try
            {
                var content = File.ReadAllText(GameFilterFlagPath).Trim();
                GameFilterProtocol = FileValueToProtocol(content);
            }
            catch { }
        }
        else
        {
            GameFilterEnabled = false;
        }

        // IPSet mode
        if (!File.Exists(IpSetFilePath))
        {
            IpSetMode = "—";
        }
        else
        {
            try
            {
                var lines = File.ReadAllLines(IpSetFilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                if (lines.Length == 0)
                    IpSetMode = "any";
                else if (lines.Length == 1 && lines[0].Trim() == "203.0.113.113/32")
                    IpSetMode = "none";
                else
                    IpSetMode = "loaded";
            }
            catch { IpSetMode = "—"; }
        }

        // Zapret service
        try
        {
            using var sc = new Process
            {
                StartInfo = new ProcessStartInfo("sc", "query zapret")
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            };
            sc.Start();
            var output = sc.StandardOutput.ReadToEnd();
            sc.WaitForExit(3000);

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "✅ Запущена";
            else if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "⏹ Остановлена";
            else if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
                ZapretServiceStatus = "⚠️ Останавливается...";
            else
                ZapretServiceStatus = "❌ Не установлена";
        }
        catch
        {
            ZapretServiceStatus = "❌ Не установлена";
        }
    }

    [RelayCommand]
    private void ToggleGameFilter()
    {
        try
        {
            var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
            Directory.CreateDirectory(utilsDir);

            if (File.Exists(GameFilterFlagPath))
            {
                File.Delete(GameFilterFlagPath);
                GameFilterEnabled = false;
                AddServiceLog("🎮 Game Filter выключен");
                Logs.Add("Game Filter выключен");
            }
            else
            {
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(GameFilterProtocol));
                GameFilterEnabled = true;
                AddServiceLog($"🎮 Game Filter включён ({GameFilterProtocol})");
                Logs.Add($"Game Filter включён ({GameFilterProtocol})");
            }

            AddServiceLog("⚠️ Перезапустите zapret для применения изменений");
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CycleIpSetMode()
    {
        try
        {
            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);

            if (IpSetMode == "loaded")
            {
                if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                if (File.Exists(IpSetFilePath)) File.Move(IpSetFilePath, IpSetBackupPath);
                File.WriteAllText(IpSetFilePath, "203.0.113.113/32\r\n");
                IpSetMode = "none";
                AddServiceLog("🔒 IPSet → none (фильтрация отключена)");
            }
            else if (IpSetMode == "none")
            {
                File.WriteAllText(IpSetFilePath, "");
                IpSetMode = "any";
                AddServiceLog("🌐 IPSet → any (все адреса)");
            }
            else
            {
                if (File.Exists(IpSetBackupPath))
                {
                    if (File.Exists(IpSetFilePath)) File.Delete(IpSetFilePath);
                    File.Move(IpSetBackupPath, IpSetFilePath);
                    IpSetMode = "loaded";
                    AddServiceLog("📋 IPSet → loaded (список восстановлен)");
                }
                else
                {
                    AddServiceLog("⚠️ Нет бэкапа IPSet. Обновите список через кнопку ниже");
                    return;
                }
            }

            AddServiceLog("⚠️ Перезапустите zapret для применения изменений");
            RefreshServiceStatus();
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateIpSetList()
    {
        IsServiceBusy = true;
        AddServiceLog("⬇️ Скачиваем ipset-all.txt...");

        try
        {
            var url = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var content = await http.GetStringAsync(url);

            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);
            await File.WriteAllTextAsync(IpSetFilePath, content);

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog($"✅ IPSet обновлён ({content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} записей)");
                RefreshServiceStatus();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog($"❌ Ошибка скачивания IPSet: {ex.Message}");
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private async Task UpdateHostsFile()
    {
        IsServiceBusy = true;
        AddServiceLog("⬇️ Проверяем hosts файл...");

        try
        {
            var hostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "FluxRoute");
            var newContent = await http.GetStringAsync(hostsUrl);
            var newLines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

            if (!File.Exists(hostsPath))
            {
                Application.Current.Dispatcher.Invoke(() => AddServiceLog("❌ Файл hosts не найден"));
                return;
            }

            var currentHosts = await File.ReadAllTextAsync(hostsPath);
            var firstLine = newLines.FirstOrDefault()?.Trim() ?? "";
            var lastLine = newLines.LastOrDefault()?.Trim() ?? "";

            if (currentHosts.Contains(firstLine) && currentHosts.Contains(lastLine))
            {
                Application.Current.Dispatcher.Invoke(() => AddServiceLog("✅ Hosts файл актуален"));
                return;
            }

            var tempFile = Path.Combine(Path.GetTempPath(), "zapret_hosts.txt");
            await File.WriteAllTextAsync(tempFile, newContent);

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddServiceLog("⚠️ Hosts нужно обновить — открыты оба файла");
                AddServiceLog($"  Источник: {tempFile}");
                AddServiceLog($"  Цель: {hostsPath}");
                Process.Start(new ProcessStartInfo("notepad.exe", tempFile) { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{hostsPath}\"") { UseShellExecute = true });
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() => AddServiceLog($"❌ Ошибка: {ex.Message}"));
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private void InstallZapretService()
    {
        if (SelectedProfile is null)
        {
            AddServiceLog("❌ Сначала выберите профиль");
            return;
        }

        AddServiceLog($"🔧 Установка службы zapret с профилем «{SelectedProfile.DisplayName}»...");
        AddServiceLog("⚠️ Запускаем service.bat — следуйте инструкциям в консоли");

        try
        {
            var serviceBat = Path.Combine(EngineDir, "service.bat");
            if (!File.Exists(serviceBat))
            {
                AddServiceLog("❌ service.bat не найден в engine/");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{serviceBat}\" admin",
                WorkingDirectory = EngineDir,
                UseShellExecute = true,
                Verb = "runas"
            });

            AddServiceLog("✅ service.bat запущен с правами администратора");
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForceStopZapretService()
    {
        if (!CustomDialog.Show(
            "⚠️ Подтверждение остановки",
            "Вы действительно хотите принудительно остановить службу zapret?\n\nВсе активные соединения через zapret будут прерваны.",
            "Остановить", "Отмена", isDanger: true)) return;

        AddServiceLog("⏹ Принудительная остановка службы zapret...");

        try
        {
            var commands = "net stop zapret >nul 2>&1 & taskkill /IM winws.exe /F >nul 2>&1 & net stop WinDivert >nul 2>&1 & echo Done & timeout /t 2";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {commands}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false
            });

            AddServiceLog("✅ Команды остановки отправлены");
            _ = Task.Delay(3000).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(RefreshServiceStatus));
        }
        catch (Exception ex)
        {
            AddServiceLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshServiceInfo()
    {
        RefreshServiceStatus();
        AddServiceLog("🔄 Статус обновлён");
    }
}
