using System.IO;
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
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 1080;
    public string Secret { get; set; } = "";
    public string Domain { get; set; } = "www.google.com";
    public bool Verbose { get; set; } = false;
    public bool PreferIPv4 { get; set; } = true;
}

public sealed class ProfileRatingEntry
{
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Score { get; set; } = 0;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _settingsPath = Path.Combine(appDir, "fluxroute-settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }
}
