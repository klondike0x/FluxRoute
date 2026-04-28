using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxRoute.Core.Services;

public sealed class AppSettings
{
    // Профиль
    public string? LastProfileFileName { get; set; }

    // Оркестратор
    public string OrchestratorInterval { get; set; } = "1";
    public bool SiteYouTube { get; set; } = true;
    public bool SiteDiscord { get; set; } = true;
    public bool SiteGoogle { get; set; } = true;
    public bool SiteTwitch { get; set; } = true;
    public bool SiteInstagram { get; set; } = true;
    public bool SiteTelegram { get; set; } = true;

    // Рейтинг профилей
    public List<ProfileRatingEntry> ProfileRatings { get; set; } = new();

    // Game Filter
    public string GameFilterProtocol { get; set; } = "TCP и UDP";

    // Обновления
    public bool AutoUpdateEnabled { get; set; } = false;

    // Системные
    public bool AutoStartEnabled { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;

    // Предупреждение при смене профиля
    public bool ShowProfileSwitchWarning { get; set; } = true;

    // TG WS Proxy
    public TgProxySettings TgProxy { get; set; } = new();
}

public sealed class TgProxySettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1443;
    public string Secret { get; set; } = "";
    public string Domain { get; set; } = "";
    public bool Verbose { get; set; } = false;
    public bool PreferIPv4 { get; set; } = true;

    // DC → IP
    public string DcIps { get; set; } = "2:149.154.167.220\n4:149.154.167.220";

    // Cloudflare Proxy
    public bool CfProxyEnabled { get; set; } = true;
    public bool CfProxyPriority { get; set; } = true;
    public bool CfDomainEnabled { get; set; } = false;
    public string CfDomain { get; set; } = "";

    // Производительность
    public int BufKb { get; set; } = 256;
    public int PoolSize { get; set; } = 4;
    public double LogMaxMb { get; set; } = 5.0;
}

public sealed class ProfileRatingEntry
{
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Score { get; set; } = 0;
}

public interface ISettingsService
{
    string SettingsPath { get; }
    string BackupPath { get; }
    bool IsPortable { get; }

    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>
/// Portable-first settings storage.
///
/// FluxRoute is currently distributed as a portable app, so settings intentionally stay
/// next to FluxRoute.exe. This service hardens the existing behavior without changing
/// the storage location: atomic save, backup, corrupt-file quarantine and defensive
/// normalization of nullable collections/nested settings.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string SettingsFileName = "fluxroute-settings.json";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public SettingsService()
    {
        var appDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);

        SettingsPath = Path.Combine(appDirectory, SettingsFileName);
        BackupPath = SettingsPath + ".bak";
        IsPortable = true;
    }

    public string SettingsPath { get; }
    public string BackupPath { get; }
    public bool IsPortable { get; }

    public AppSettings Load()
    {
        if (TryLoad(SettingsPath, out var settings))
        {
            return Normalize(settings);
        }

        if (TryLoad(BackupPath, out var backupSettings))
        {
            TryRestoreBackupAsPrimary();
            return Normalize(backupSettings);
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var normalized = Normalize(settings);
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(tempPath, json, Utf8NoBom);

            ReplaceFileAtomically(tempPath, SettingsPath, BackupPath);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"FluxRoute settings save failed. Path='{SettingsPath}'. Error='{ex}'");
            TryDeleteTempFile(tempPath);
        }
    }

    private static bool TryLoad(string path, out AppSettings settings)
    {
        settings = new AppSettings();

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path, Utf8NoBom);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"FluxRoute settings load failed. Path='{path}'. Error='{ex}'");
            return false;
        }
    }

    private void TryRestoreBackupAsPrimary()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(SettingsPath))
            {
                var corruptPath = $"{SettingsPath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmss}";
                File.Move(SettingsPath, corruptPath, overwrite: true);
            }

            File.Copy(BackupPath, SettingsPath, overwrite: true);
            Trace.TraceWarning($"FluxRoute settings restored from backup. Backup='{BackupPath}', Target='{SettingsPath}'.");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"FluxRoute settings backup restore failed. Backup='{BackupPath}', Target='{SettingsPath}'. Error='{ex}'");
        }
    }

    private static void ReplaceFileAtomically(string tempPath, string targetPath, string backupPath)
    {
        if (File.Exists(targetPath))
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            // On NTFS File.Replace is atomic and keeps the previous valid config in *.bak.
            File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, targetPath);
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // The next save uses a unique temp filename, so a failed cleanup is not fatal.
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.ProfileRatings ??= new List<ProfileRatingEntry>();
        settings.TgProxy ??= new TgProxySettings();

        // Миграция: сброс старого дефолтного домена www.google.com.
        // fake-tls-domain требует домен, указывающий на IP самого прокси.
        // Для локального 127.0.0.1 fake-tls не нужен, пустая строка = выкл.
        if (settings.TgProxy.Domain == "www.google.com")
        {
            settings.TgProxy.Domain = "";
        }

        return settings;
    }
}
