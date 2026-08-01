// ═══ v1.7.0: Zapret2CompatibilityService ═══
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FluxRoute.Core.Models;

namespace FluxRoute.Core.Services;

public class Zapret2CompatibilityService : IZapret2CompatibilityService
{
    public Zapret2Capability CheckCapability()
    {
        var w = new List<string>(); var e = new List<string>(); bool ok = true;
        if (!IsAdmin()) { w.Add("Требуются права администратора"); ok = false; }
        var arch = RuntimeInformation.OSArchitecture.ToString();
        if (arch != "X64") w.Add($"Архитектура {arch} может не поддерживаться");
        var ver = $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
        if (Environment.OSVersion.Version.Major < 10) { e.Add("Требуется Windows 10+"); ok = false; }
        var wd = CheckWinDivert(); var wdv = wd ? GetWinDivertVer() : null;
        if (!wd) w.Add("WinDivert не найден — будет загружен автоматически");
        return new(ok, wd, wdv, IsAdmin(), arch, ver, w, e);
    }

    public Task<Zapret2Capability> CheckCapabilityAsync(CancellationToken ct = default) => Task.FromResult(CheckCapability());

    private static bool CheckWinDivert() => new[] { Path.Combine(Environment.SystemDirectory, "WinDivert.dll"), Path.Combine(Environment.SystemDirectory, "WinDivert64.sys"), Path.Combine(AppContext.BaseDirectory, "WinDivert.dll") }.Any(File.Exists);
    private static string? GetWinDivertVer() { try { foreach (var p in new[] { Path.Combine(Environment.SystemDirectory, "WinDivert.dll") }) { if (File.Exists(p)) return FileVersionInfo.GetVersionInfo(p).FileVersion; } } catch { } return null; }
    private static bool IsAdmin() { using var i = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(i).IsInRole(WindowsBuiltInRole.Administrator); }
}
