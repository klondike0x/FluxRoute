using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Windows;
using FluxRoute.AI.Services;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.Services;
using FluxRoute.Updater.Services;
using FluxRoute.ViewModels;
using FluxRoute.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using Serilog;
using Serilog.Events;
using Application = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace FluxRoute;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ═══ ГЛОБАЛЬНАЯ НАСТРОЙКА SSL/TLS ═══
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 |
            System.Net.SecurityProtocolType.Tls13;
        // ════════════════════════════════════

        Log.Logger = ConfigureSerilog(new LoggerConfiguration()).CreateLogger();

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog((_, _, loggerConfiguration) => ConfigureSerilog(loggerConfiguration))
                .ConfigureServices(ConfigureApplicationServices)
                .Build();

            await _host.StartAsync();

            Log.Information("FluxRoute application host started. Arguments: {Arguments}", e.Args);

            if (!IsRunningAsAdmin())
            {
                Log.Warning("FluxRoute is running without administrator privileges.");

                // Временно переключаем, чтобы закрытие диалога не завершило приложение.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var prompt = new AdminPromptWindow();
                prompt.ShowDialog();

                if (!prompt.ContinueWithoutAdmin)
                {
                    Log.Information("User declined to continue without administrator privileges.");
                    Shutdown();
                    return;
                }
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "FluxRoute failed to start.");

            WpfMessageBox.Show(
                $"FluxRoute не удалось запустить.\n\n{ex.Message}",
                "Критическая ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                Log.Information("Stopping FluxRoute application host.");
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FluxRoute application host stopped with errors.");
        }
        finally
        {
            Log.Information("FluxRoute application exited with code {ExitCode}.", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static void ConfigureApplicationServices(IServiceCollection services)
    {
        // ── Клиент для проверки версии обновлений движка (короткие запросы) ──────
        // Стандартный resilience handler с настроенными таймаутами:
        // - TotalRequestTimeout: 60с (вся операция с учётом retry)
        // - AttemptTimeout: 30с (одна попытка)
        // - SamplingDuration: 60с (>= 2 * AttemptTimeout, требование circuit breaker)
        // - Retry: 3 попытки с экспоненциальной задержкой + jitter
        services.AddHttpClient(FluxRoute.Updater.Services.HttpClientNames.Updater, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
        })
        .AddStandardResilienceHandler()
        .Configure(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(1);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
        });

        // ── Клиент для скачивания ZIP-архива обновлений (большие файлы) ──────────
        // Явный HttpClientHandler с SslProtocols — SocketsHttpHandler НЕ использует
        // ServicePointManager.SecurityProtocol, а на некоторых системах дефолт не включает Tls12/Tls13.
        // - TotalRequestTimeout: 300с (5 минут)
        // - AttemptTimeout: 120с (2 минуты на попытку)
        // - SamplingDuration: 240с (>= 2 * AttemptTimeout)
        // - Retry: 3 попытки с экспоненциальной задержкой + jitter
        services.AddHttpClient(FluxRoute.Updater.Services.HttpClientNames.UpdaterDownload, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Updater");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            UseProxy = true,
        })
        .AddStandardResilienceHandler()
        .Configure(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240);
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
        });

        // Named HttpClient для проверки обновлений самого приложения FluxRoute (GitHub API + Atom)
        services.AddHttpClient(FluxRoute.Updater.Services.HttpClientNames.AppUpdater, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-AppUpdater");
        })
        .AddStandardResilienceHandler();

        // Named HttpClient для проверки связности (оркестратор)
        services.AddHttpClient(FluxRoute.Core.Services.HttpClientNames.Connectivity, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        })
        .AddStandardResilienceHandler();

        // ═══ НОВЫЙ: Named HttpClient для ServiceViewModel (IPSet, Hosts) ═══
        services.AddHttpClient("Service", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Service");
            client.DefaultRequestHeaders.Add("Accept", "text/plain,application/json");
        })
        .AddStandardResilienceHandler();
        // ════════════════════════════════════════════════════════════════════

        // Named HttpClient для скачивания TG WS Proxy
        services.AddHttpClient("TgProxyDownloader", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "FluxRoute-Desktop/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
        })
        .AddStandardResilienceHandler();
        // ════════════════════════════════════════════════════════════════

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdaterService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UpdaterService>>();
            var settings = sp.GetRequiredService<ISettingsService>();

            // ═══ v1.6.0 (#60): Передаём функцию получения зеркал из настроек ═══
            Func<IReadOnlyList<string>>? getVersionMirrors = () =>
            {
                var s = settings.Load();
                if (s.FallbackMirrors.TryGetValue("engine.version", out var raw)
                    && !string.IsNullOrWhiteSpace(raw))
                {
                    return raw.Split(new[] { '\n', '\r', ',' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                return Array.Empty<string>();
            };
            // ═══════════════════════════════════════════════════════════════

            return new UpdaterService(factory, logger, getVersionMirrors);
        });
        services.AddSingleton<IAppUpdaterService, AppUpdaterService>();
        services.AddSingleton<IConnectivityChecker, ConnectivityChecker>();
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();

        services.AddSingleton<NetworkFingerprintProvider>();
        services.AddSingleton(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var dir = Path.GetDirectoryName(settingsService.SettingsPath)!;
            var registryPath = Path.Combine(dir, "fluxroute-ai-strategies.json");
            var registry = new AiStrategyRegistry(registryPath);
            registry.Load();
            return registry;
        });
        services.AddSingleton(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var dir = Path.GetDirectoryName(settingsService.SettingsPath)!;
            return new AiHistoryStore(Path.Combine(dir, "fluxroute-ai-history.jsonl"));
        });
        services.AddSingleton<BatMaterializer>();
        services.AddSingleton(sp =>
            new BanditSelector(sp.GetRequiredService<AiStrategyRegistry>(), new Random()));
        services.AddSingleton(sp =>
            new StrategyEvolver(
                sp.GetRequiredService<AiStrategyRegistry>(),
                sp.GetRequiredService<AiHistoryStore>(),
                sp.GetRequiredService<BatMaterializer>(),
                () => Path.Combine(AppContext.BaseDirectory, "engine"),
                () => sp.GetRequiredService<ISettingsService>().Load().Ai));

        services.AddSingleton(sp =>
            new NetworkChangeWatcher(sp.GetRequiredService<NetworkFingerprintProvider>()));

        services.AddSingleton<MainViewModel>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var updaterService = sp.GetRequiredService<IUpdaterService>();
            var appUpdaterService = sp.GetRequiredService<IAppUpdaterService>();
            var connectivity = sp.GetRequiredService<IConnectivityChecker>();
            var fingerprints = sp.GetRequiredService<NetworkFingerprintProvider>();
            var networkWatcher = sp.GetRequiredService<NetworkChangeWatcher>();
            var registry = sp.GetRequiredService<AiStrategyRegistry>();
            var historyStore = sp.GetRequiredService<AiHistoryStore>();
            var bandit = sp.GetRequiredService<BanditSelector>();
            var evolver = sp.GetRequiredService<StrategyEvolver>();
            var materializer = sp.GetRequiredService<BatMaterializer>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var taskScheduler = sp.GetRequiredService<ITaskSchedulerService>();
            var trayIcon = sp.GetRequiredService<TrayIconService>();

            return new MainViewModel(
                settingsService,
                updaterService,
                appUpdaterService,
                connectivity,
                fingerprints,
                networkWatcher,
                registry,
                historyStore,
                bandit,
                evolver,
                materializer,
                httpClientFactory,
                taskScheduler,
                trayIcon);
        });
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MainWindow>();
    }

    private static LoggerConfiguration ConfigureSerilog(LoggerConfiguration loggerConfiguration)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxRoute",
            "logs");

        Directory.CreateDirectory(logDirectory);

        return loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "fluxroute-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
