using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clipboard = System.Windows.Clipboard;

namespace FluxRoute.ViewModels;

/// <summary>
/// Feature ViewModel для вкладки "Диагностика".
/// Изолирует диагностические проверки и состояние от MainViewModel.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly Func<string> _getEngineDir;
    private readonly Func<string> _getWinwsPath;
    private readonly Func<string> _getWinDivertDllPath;
    private readonly Func<string> _getWinDivertSys64Path;
    private readonly Func<string> _getWinDivertSysPath;
    private readonly Action<string> _addAppLog;

    [ObservableProperty] private bool isAdmin;
    [ObservableProperty] private bool engineOk;
    [ObservableProperty] private bool winwsOk;
    [ObservableProperty] private bool winDivertDllOk;
    [ObservableProperty] private bool winDivertDriverOk;

    public string AdminText => IsAdmin ? "✅ Да" : "❌ Нет";
    public string EngineText => EngineOk ? "✅ Найдено" : "❌ Не найдено";
    public string WinwsText => WinwsOk ? "✅ Найдено" : "❌ Не найдено";
    public string WinDivertDllText => WinDivertDllOk ? "✅ Найдено" : "❌ Не найдено";
    public string WinDivertDriverText => WinDivertDriverOk ? "✅ Найдено" : "❌ Не найдено";

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(AdminText));
    partial void OnEngineOkChanged(bool value) => OnPropertyChanged(nameof(EngineText));
    partial void OnWinwsOkChanged(bool value) => OnPropertyChanged(nameof(WinwsText));
    partial void OnWinDivertDllOkChanged(bool value) => OnPropertyChanged(nameof(WinDivertDllText));
    partial void OnWinDivertDriverOkChanged(bool value) => OnPropertyChanged(nameof(WinDivertDriverText));

    public DiagnosticsViewModel(
        Func<string> getEngineDir,
        Func<string> getWinwsPath,
        Func<string> getWinDivertDllPath,
        Func<string> getWinDivertSys64Path,
        Func<string> getWinDivertSysPath,
        Action<string> addAppLog)
    {
        _getEngineDir = getEngineDir;
        _getWinwsPath = getWinwsPath;
        _getWinDivertDllPath = getWinDivertDllPath;
        _getWinDivertSys64Path = getWinDivertSys64Path;
        _getWinDivertSysPath = getWinDivertSysPath;
        _addAppLog = addAppLog;
    }

    public void Refresh()
    {
        IsAdmin = CheckIsAdmin();
        EngineOk = Directory.Exists(_getEngineDir());
        WinwsOk = File.Exists(_getWinwsPath());
        WinDivertDllOk = File.Exists(_getWinDivertDllPath());
        WinDivertDriverOk = File.Exists(_getWinDivertSys64Path()) || File.Exists(_getWinDivertSysPath());
    }

    public static bool CheckIsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return asm.GetName().Version?.ToString(3) ?? "—";
    }

    public string BuildDiagnosticsText(
        string appVersion,
        string statusText,
        string runningScriptName,
        string pidText,
        string uptimeText,
        bool orchestratorRunning)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FluxRoute Desktop");
        sb.AppendLine($"Version: {appVersion}");
        sb.AppendLine($"Admin: {(IsAdmin ? "Yes" : "No")}");
        sb.AppendLine($"Engine: {EngineText} ({_getEngineDir()})");
        sb.AppendLine($"winws.exe: {WinwsText}");
        sb.AppendLine($"WinDivert.dll: {WinDivertDllText}");
        sb.AppendLine($"WinDivert.sys: {WinDivertDriverText}");
        sb.AppendLine($"Status: {statusText}");
        sb.AppendLine($"Running BAT: {runningScriptName}");
        sb.AppendLine($"PID: {pidText}");
        sb.AppendLine($"Uptime: {uptimeText}");
        sb.AppendLine($"Orchestrator: {(orchestratorRunning ? "Running" : "Stopped")}");
        return sb.ToString();
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            var text = BuildDiagnosticsText("—", "—", "—", "—", "—", false);
            Clipboard.SetText(text);
            _addAppLog("Диагностика скопирована.");
        }
        catch (Exception ex)
        {
            _addAppLog($"Ошибка: {ex.Message}");
        }
    }
}
