namespace FluxRoute.Updater.Services;

/// <summary>
/// v1.6.0 (#60, #58): Зеркала для загрузки engine, ipset, hosts и tg-proxy.
/// Если основной источник Flowseal недоступен — пробуются зеркала из этого класса
/// и из пользовательских настроек AppSettings.FallbackMirrors.
/// </summary>
public static class MirrorUrls
{
    // ═══ Engine (zapret-discord-youtube) ═══

    /// <summary>Основной URL версии движка (GitHub Flowseal).</summary>
    public const string EngineVersion =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";

    /// <summary>SourceForge-зеркало: версия движка.</summary>
    public const string EngineVersionMirrorSf =
        "https://sourceforge.net/projects/zapret-discord-youtube.mirror/files/.service/version.txt/download";

    /// <summary>Шаблон ZIP-архива движка (GitHub Flowseal, подставляется версия).</summary>
    public const string EngineZipTemplate =
        "https://github.com/Flowseal/zapret-discord-youtube/releases/download/{0}/zapret-discord-youtube-{0}.zip";

    /// <summary>SourceForge-зеркало: шаблон ZIP-архива движка (подставляется версия).</summary>
    public const string EngineZipTemplateMirrorSf =
        "https://sourceforge.net/projects/zapret-discord-youtube.mirror/files/zapret-discord-youtube-{0}.zip/download";

    // ═══ IPSet + Hosts ═══

    /// <summary>IPSet-список (GitHub Flowseal).</summary>
    public const string IpSetList =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";

    /// <summary>SourceForge-зеркало: IPSet-список.</summary>
    public const string IpSetListMirrorSf =
        "https://sourceforge.net/projects/zapret-discord-youtube.mirror/files/.service/ipset-service.txt/download";

    /// <summary>Hosts-файл (GitHub Flowseal).</summary>
    public const string HostsFile =
        "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";

    /// <summary>SourceForge-зеркало: Hosts-файл.</summary>
    public const string HostsFileMirrorSf =
        "https://sourceforge.net/projects/zapret-discord-youtube.mirror/files/.service/hosts/download";

    // ═══ TG WS Proxy ═══

    /// <summary>Atom-лента релизов tg-ws-proxy (GitHub Flowseal).</summary>
    public const string TgProxyReleasesAtom =
        "https://github.com/Flowseal/tg-ws-proxy/releases.atom";

    /// <summary>SourceForge-зеркало: Atom-лента релизов tg-ws-proxy.</summary>
    public const string TgProxyReleasesAtomMirrorSf =
        "https://sourceforge.net/projects/tg-ws-proxy.mirror/files/releases.atom/download";

    /// <summary>Шаблон ZIP-архива tg-ws-proxy (GitHub Flowseal, подставляется тег).</summary>
    public const string TgProxyZipTemplate =
        "https://github.com/Flowseal/tg-ws-proxy/archive/{0}.zip";

    /// <summary>SourceForge-зеркало: шаблон ZIP-архива tg-ws-proxy (подставляется тег).</summary>
    public const string TgProxyZipTemplateMirrorSf =
        "https://sourceforge.net/projects/tg-ws-proxy.mirror/files/{0}.zip/download";

    /// <summary>Последний релиз tg-ws-proxy (GitHub Flowseal, редирект).</summary>
    public const string TgProxyLatestRelease =
        "https://github.com/Flowseal/tg-ws-proxy/releases/latest";

    /// <summary>SourceForge-зеркало: последний релиз tg-ws-proxy.</summary>
    public const string TgProxyLatestReleaseMirrorSf =
        "https://sourceforge.net/projects/tg-ws-proxy.mirror/files/latest/download";
}
