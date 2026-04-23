namespace FluxRoute.Core.Models;

public sealed class ProfileProbeOptions
{
    /// <summary>Пауза после запуска BAT-профиля перед проверкой сети.</summary>
    public TimeSpan StartupWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Дополнительная пауза для проверки, что winws.exe не умер сразу после старта.</summary>
    public TimeSpan StableWait { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Сколько ждать появления winws.exe.</summary>
    public TimeSpan ProcessWaitTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Останавливать профиль после проверки. Нужно для полного сканирования всех профилей.</summary>
    public bool StopAfterProbe { get; init; }

    /// <summary>Если true, профиль без winws.exe получает 0%, даже если обычный интернет работает.</summary>
    public bool RequireWinwsProcess { get; init; } = true;

    /// <summary>HTTP-проверки выполнять через curl.exe, как в zapret/blockcheck-подобных тестах.</summary>
    public bool UseCurlForHttp { get; init; } = true;

    /// <summary>Сколько целей проверять параллельно. Не ставь слишком много: каждый HTTP-тест запускает curl.exe.</summary>
    public int MaxParallelChecks { get; init; } = 6;

    public string ProcessName { get; init; } = "winws";
}
