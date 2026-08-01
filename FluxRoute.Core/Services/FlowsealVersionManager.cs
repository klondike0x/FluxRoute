// ═══ v1.7.0: FlowsealVersionManager ═══
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using FluxRoute.Core.Models;
using Microsoft.Extensions.Logging;

namespace FluxRoute.Core.Services;

public class FlowsealVersionManager : IFlowsealVersionManager
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<FlowsealVersionManager> _log;
    public FlowsealVersionManager(IHttpClientFactory http, ILogger<FlowsealVersionManager> log) { _http = http; _log = log; }

    public async Task<IReadOnlyList<FlowsealVersion>> GetAvailableVersionsAsync(CancellationToken ct = default)
    {
        try
        {
            var c = _http.CreateClient("FluxRoute.Updater");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FluxRoute");
            var r = await c.GetAsync("https://api.github.com/repos/bol-van/zapret/releases", ct);
            if (!r.IsSuccessStatusCode) return Array.Empty<FlowsealVersion>();
            var json = await r.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(release =>
            {
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                var url = release.TryGetProperty("assets", out var a) && a.GetArrayLength() > 0 && a[0].TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                return new FlowsealVersion(tag, DownloadUrl: url, Source: "GitHub");
            }).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "Ошибка получения версий Flowseal"); return Array.Empty<FlowsealVersion>(); }
    }

    public async Task<FlowsealInstallResult> InstallVersionAsync(string version, string engineDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version)) return new(false, version, "Версия не указана");
        if (!Directory.Exists(engineDir)) return new(false, version, $"Папка не найдена: {engineDir}");
        try
        {
            var versions = await GetAvailableVersionsAsync(ct);
            var target = versions.FirstOrDefault(v => v.Version == version || v.Version == $"v{version}");
            if (target?.DownloadUrl is null) return new(false, version, "Версия не найдена");

            var staging = Path.Combine(Path.GetTempPath(), $"fs_staging_{Guid.NewGuid():N}");
            var zip = Path.Combine(staging, "flowseal.zip");
            Directory.CreateDirectory(staging);

            var c = _http.CreateClient("FluxRoute.UpdaterDownload");
            await using var s = await c.GetStreamAsync(target.DownloadUrl, ct);
            await using var fs = File.Create(zip); await s.CopyToAsync(fs, ct);

            var ext = Path.Combine(staging, "extracted");
            ZipFile.ExtractToDirectory(zip, ext);

            // очистка + копирование
            foreach (var f in Directory.GetFiles(engineDir)) File.Delete(f);
            foreach (var d in Directory.GetDirectories(engineDir)) Directory.Delete(d, true);
            CopyDir(ext, engineDir);

            try { Directory.Delete(staging, true); } catch { }
            return new(true, version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, version, ex.Message); }
    }

    public Task<FlowsealInstallResult> RollbackAsync(string engineDir, CancellationToken ct = default)
        => Task.FromResult(new FlowsealInstallResult(false, "?", "Откат не настроен"));

    public string? GetCurrentVersion(string engineDir)
    {
        var f = Path.Combine(engineDir, "version.txt");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }

    public async Task<FlowsealVersion?> CheckForUpdateAsync(string engineDir, CancellationToken ct = default)
    {
        var cur = GetCurrentVersion(engineDir);
        var vers = await GetAvailableVersionsAsync(ct);
        if (vers.Count == 0) return null;
        if (cur is null) return vers[0];
        return vers[0].Version.TrimStart('v') != cur.TrimStart('v') ? vers[0] : null;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
