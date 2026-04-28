using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using FluxRoute.Core.Services;
using Application = System.Windows.Application;

namespace FluxRoute.ViewModels;

public partial class MainViewModel
{
    #region Win32 API

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // Делегат оставлен, потому что поле _winEventCallback объявлено в MainViewModel.cs.
    // Сам WinEventHook больше не устанавливаем: при массовом сканировании профилей он мог
    // ломать WPF message pump и приводить к System.ExecutionEngineException в Dispatcher.cs.
    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    #endregion

    private bool _startedViaBatFallback;

    [RelayCommand]
    private void RefreshProfiles()
    {
        Logs.Add("Обновляем список профилей...");
        LoadProfiles();
        RefreshDiagnostics();
    }

    [RelayCommand]
    private void ToggleStartStop()
    {
        if (IsRunning)
            Stop();
        else
            Start();
    }

    [RelayCommand]
    private void Start()
    {
        if (IsRunning)
        {
            Logs.Add("Процесс уже запущен.");
            return;
        }

        if (SelectedProfile is null)
        {
            Logs.Add("Профиль не выбран.");
            AddToRecentLogs("❌ Профиль не выбран");
            return;
        }

        if (!File.Exists(SelectedProfile.FullPath))
        {
            Logs.Add($"BAT не найден: {SelectedProfile.FullPath}");
            AddToRecentLogs("❌ BAT не найден");
            return;
        }

        try
        {
            ProfileBatLauncher.PrepareRuntime(EngineDir);

            if (ProfileBatLauncher.TryCreateLaunchPlan(SelectedProfile.FullPath, EngineDir, out var plan, out var parseError) && plan is not null)
            {
                StartWinwsDirect(plan);
                return;
            }

            Logs.Add($"⚠️ Прямой запуск winws.exe недоступен: {parseError}");
            Logs.Add("⚠️ Использую совместимый запуск через BAT/cmd.exe.");
            StartViaBatFallback();
        }
        catch (Exception ex)
        {
            Logs.Add($"Ошибка запуска: {ex.Message}");
            AddToRecentLogs("❌ Ошибка запуска");
        }
    }

    private void StartWinwsDirect(WinwsLaunchPlan plan)
    {
        var winws = ProfileBatLauncher.StartWinws(plan);
        _startedViaBatFallback = false;
        _trackedPids = new HashSet<uint> { (uint)winws.Id };

        StatusText = "Запущено";
        CurrentStrategy = SelectedProfile?.DisplayName ?? "—";
        RunningScriptName = SelectedProfile?.FileName ?? "—";
        _runStartedAt = DateTimeOffset.Now;

        Logs.Add($"Прямой запуск winws.exe: {RunningScriptName}");
        Logs.Add($"winws.exe запущен как дочерний процесс FluxRoute, PID: {winws.Id}");
        AddToRecentLogs($"✅ Запущен (PID: {winws.Id})");

        _ = TrackDirectWinwsAsync(winws);

        RefreshDiagnostics();
        UpdateRuntimeInfo();
    }

    private void StartViaBatFallback()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{SelectedProfile!.FullPath}\"",
            WorkingDirectory = EngineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        var cmdProcess = Process.Start(psi);
        if (cmdProcess is null)
        {
            Logs.Add("Не удалось запустить процесс.");
            AddToRecentLogs("❌ Ошибка запуска");
            return;
        }

        _startedViaBatFallback = true;
        StatusText = "Запущено";
        CurrentStrategy = SelectedProfile.DisplayName;
        RunningScriptName = SelectedProfile.FileName;
        _runStartedAt = DateTimeOffset.Now;

        Logs.Add($"Запуск через BAT: {RunningScriptName}");
        AddToRecentLogs($"▶ Запуск: {RunningScriptName}");

        _ = TrackWinwsAsync(cmdProcess);

        RefreshDiagnostics();
        UpdateRuntimeInfo();
    }

    private async Task TrackDirectWinwsAsync(Process winws)
    {
        _hideWindowsCts?.Cancel();
        _hideWindowsCts = new CancellationTokenSource();
        var ct = _hideWindowsCts.Token;

        try
        {
            // Даём процессу короткое время на запуск и обновляем дерево PID без WinEventHook.
            for (var i = 0; i < 20 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);

                try
                {
                    var pids = GetProcessTreePids((uint)winws.Id);
                    pids.Add((uint)winws.Id);
                    _trackedPids = pids;
                }
                catch
                {
                    // Процесс мог завершиться между проверками.
                }
            }

            await RunOnUiThreadAsync(() =>
            {
                _runningProcess = winws;
                PidText = SafePidText(winws);
                IsRunning = !SafeHasExited(winws);
            }).ConfigureAwait(false);

            await winws.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stop() was requested.
        }
        catch (ObjectDisposedException)
        {
            // Процесс уже был очищен при Stop().
        }
        catch (InvalidOperationException)
        {
            // Процесс мог завершиться/стать недоступным во время переключения профилей.
        }

        if (!ct.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusText = "Не запущено";
                CurrentStrategy = "—";
                RunningScriptName = "—";
                PidText = "—";
                UptimeText = "—";
                _runStartedAt = null;
                _runningProcess = null;
                IsRunning = false;
                Logs.Add("winws.exe завершился.");
                AddToRecentLogs("⏹ Завершён");
            }).ConfigureAwait(false);
        }
    }

    private async Task TrackWinwsAsync(Process cmdProcess)
    {
        _hideWindowsCts?.Cancel();
        _hideWindowsCts = new CancellationTokenSource();
        var ct = _hideWindowsCts.Token;
        Process? winws = null;

        try
        {
            for (var i = 0; i < 100 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);

                var rootPid = SafeHasExited(cmdProcess) ? 0 : (uint)cmdProcess.Id;
                var pids = rootPid > 0 ? GetProcessTreePids(rootPid) : new HashSet<uint>();

                try
                {
                    foreach (var c in Process.GetProcessesByName("winws"))
                        pids.Add((uint)c.Id);
                }
                catch
                {
                    // ignored
                }

                _trackedPids = pids;

                if (winws is null)
                {
                    try
                    {
                        winws = Process.GetProcessesByName("winws").FirstOrDefault();
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (winws is not null)
                    break;
            }

            if (winws is null)
            {
                await RunOnUiThreadAsync(() =>
                {
                    Logs.Add("winws.exe не найден после запуска BAT.");
                    AddToRecentLogs("❌ winws.exe не найден");
                }).ConfigureAwait(false);
                return;
            }

            _trackedPids = new HashSet<uint>(_trackedPids) { (uint)winws.Id };

            await RunOnUiThreadAsync(() =>
            {
                _runningProcess = winws;
                PidText = SafePidText(winws);
                IsRunning = !SafeHasExited(winws);
                Logs.Add($"winws.exe запущен, PID: {winws.Id}");
                AddToRecentLogs($"✅ Запущен (PID: {winws.Id})");
            }).ConfigureAwait(false);

            await winws.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stop() was requested.
        }
        catch (ObjectDisposedException)
        {
            // Процесс уже был очищен при Stop().
        }
        catch (InvalidOperationException)
        {
            // Процесс мог завершиться/стать недоступным во время переключения профилей.
        }

        if (!ct.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusText = "Не запущено";
                CurrentStrategy = "—";
                RunningScriptName = "—";
                PidText = "—";
                UptimeText = "—";
                _runStartedAt = null;
                _runningProcess = null;
                IsRunning = false;
                Logs.Add("winws.exe завершился.");
                AddToRecentLogs("⏹ Завершён");
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _hideWindowsCts?.Cancel();

        var pidsToKill = new HashSet<uint>(_trackedPids);

        if (_runningProcess is not null && !SafeHasExited(_runningProcess))
        {
            try
            {
                pidsToKill.UnionWith(GetProcessTreePids((uint)_runningProcess.Id));
            }
            catch
            {
                // ignored
            }
        }

        // In direct mode FluxRoute kills only its own tracked winws.exe.
        // In BAT fallback mode we keep the old behavior for compatibility with profiles launched via cmd/start.
        if (_startedViaBatFallback)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("winws"))
                    pidsToKill.Add((uint)p.Id);
            }
            catch
            {
                // ignored
            }
        }

        if (pidsToKill.Count == 0)
        {
            Logs.Add("Нет запущенного процесса.");
            AddToRecentLogs("⏹ Нет активного процесса");
            StatusText = "Не запущено";
            IsRunning = false;
            return;
        }

        var killed = 0;
        foreach (var pid in pidsToKill)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                p.Kill(entireProcessTree: true);
                p.WaitForExit(1500);
                killed++;
            }
            catch
            {
                // ignored
            }
        }

        Logs.Add($"Остановлено процессов: {killed} ({RunningScriptName})");
        AddToRecentLogs($"⏹ Остановлено: {RunningScriptName}");

        _trackedPids = [];
        _runningProcess = null;
        _startedViaBatFallback = false;

        StatusText = "Остановлено";
        CurrentStrategy = "—";
        RunningScriptName = "—";
        PidText = "—";
        UptimeText = "—";
        IsRunning = false;
        _runStartedAt = null;

        RefreshDiagnostics();
        UpdateRuntimeInfo();
    }

    private static HashSet<uint> GetProcessTreePids(uint rootPid)
    {
        var pids = new HashSet<uint> { rootPid };
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE || snapshot == IntPtr.Zero)
            return pids;

        try
        {
            var entries = new List<(uint pid, uint parentPid)>();
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };

            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    entries.Add((entry.th32ProcessID, entry.th32ParentProcessID));
                }
                while (Process32Next(snapshot, ref entry));
            }

            var queue = new Queue<uint>();
            queue.Enqueue(rootPid);

            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                foreach (var (pid, parentPid) in entries)
                {
                    if (parentPid == parent && pids.Add(pid))
                        queue.Enqueue(pid);
                }
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return pids;
    }

    private void InstallWindowHook()
    {
        // Отключено намеренно. CreateNoWindow=true скрывает winws/cmd без глобального WinEventHook.
    }

    private void RemoveWindowHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            try
            {
                UnhookWinEvent(_winEventHook);
            }
            catch
            {
                // ignored
            }

            _winEventHook = IntPtr.Zero;
        }

        _winEventCallback = null;
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static string SafePidText(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch
        {
            return "—";
        }
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        if (app.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return app.Dispatcher.InvokeAsync(action).Task;
    }
}
