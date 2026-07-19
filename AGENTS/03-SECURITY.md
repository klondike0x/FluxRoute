# 🛡️ 03. Безопасность и конфигурация

---

## IOptions<T> — единственно верный путь

### appsettings.json

```json
{
  "Api": {
    "BaseUrl": "https://api.fluxroute.io",
    "ApiKey": "",
    "TimeoutSeconds": 30
  },
  "Serilog": {
    "MinimumLevel": "Information"
  },
  "Updater": {
    "GitHubRepo": "klondike0x/FluxRoute"
  }
}
```

### Класс конфигурации

```csharp
// FluxRoute.Core/Configuration/ApiSettings.cs
public class ApiSettings
{
    public const string SectionName = "Api";
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
}
```

### Регистрация в DI

```csharp
// App.xaml.cs
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();

    // Загрузка конфигурации
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()  // для CI/CD
        .Build();

    // Регистрация IOptions<T>
    services.Configure<ApiSettings>(config.GetSection(ApiSettings.SectionName));
    services.Configure<UpdaterSettings>(config.GetSection(UpdaterSettings.SectionName));

    // Остальные сервисы
    services.AddSingleton<IDataService, DataService>();

    return services.BuildServiceProvider();
}
```

### Использование в сервисе

```csharp
public class DataService : IDataService
{
    private readonly IOptions<ApiSettings> _options;
    private readonly HttpClient _client;

    public DataService(IHttpClientFactory factory, IOptions<ApiSettings> options)
    {
        _options = options;
        _client = factory.CreateClient("FluxApi");
    }

    public async Task<Result<Data>> GetDataAsync(CancellationToken ct = default)
    {
        var settings = _options.Value;  // Безопасный доступ
        var url = $"{settings.BaseUrl}/api/data";
        // ...
    }
}
```

---

## Валидация на старте

```csharp
// App.xaml.cs
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var apiSettings = Services.GetRequiredService<IOptions<ApiSettings>>().Value;

        // Проверка критических настроек
        if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
        {
            Log.Fatal("Api:BaseUrl не настроен");
            Shutdown(1);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiSettings.ApiKey))
        {
            Log.Warning("Api:ApiKey пустой — API запросы будут падать");
        }

        if (apiSettings.TimeoutSeconds <= 0)
        {
            Log.Fatal("Api:TimeoutSeconds должен быть > 0");
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }
}
```

---

## Управление секретами

### Локальная разработка

**appsettings.json** (коммитим в Git, БЕЗ секретов):
```json
{
  "Api": {
    "BaseUrl": "https://api.dev.local",
    "ApiKey": "",
    "TimeoutSeconds": 30
  }
}
```

**appsettings.Development.json** (в `.gitignore`, локально):
```json
{
  "Api": {
    "ApiKey": "sk-1234567890-dev"
  }
}
```

### CI/CD (GitHub Actions)

**.github/workflows/build.yml:**
```yaml
env:
  API_KEY: ${{ secrets.API_KEY }}
  BASE_URL: https://api.prod.io

run: |
  echo "Building with API_KEY=$API_KEY"
  dotnet build
```

---

## Логирование (безопасность)

### ❌ ПЛОХО (логирует секреты)

```csharp
_logger.LogInformation("API запрос: {Url} с ключом {ApiKey}",
    url, settings.ApiKey);  // УТЕЧКА!
```

### ✅ ХОРОШО (маскирует)

```csharp
_logger.LogInformation("API запрос: {Url}", url);

// Или если нужна отладка:
_logger.LogDebug("API Key: {MaskedKey}",
    string.IsNullOrEmpty(settings.ApiKey) ? "<empty>" : "***masked***");
```

### Пример правильного логирования

```csharp
public async Task<Result<Data>> GetDataAsync(string dataId, CancellationToken ct = default)
{
    try
    {
        var settings = _options.Value;
        var url = $"{settings.BaseUrl}/api/data/{dataId}";

        _logger.LogInformation("Запрос данных для ID: {DataId}", dataId);

        var response = await _client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var data = await response.Content
            .ReadFromJsonAsync<Data>(cancellationToken: ct)
            .ConfigureAwait(false);

        _logger.LogInformation("Успешно получены данные для ID: {DataId}", dataId);
        return Result<Data>.Ok(data!);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка при запросе данных для ID: {DataId}", dataId);
        return Result<Data>.Fail("Не удалось получить данные", ex);
    }
}
```

---

## Environment-specific конфигурация

```csharp
// App.xaml.cs
private static IServiceProvider ConfigureServices()
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{environment}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var services = new ServiceCollection();

    // Environment-specific логирование
    services.AddLogging(builder =>
    {
        if (environment == "Development")
            builder.AddDebug();
        else
            builder.AddConsole();

        builder.AddSerilog();
    });

    // Остальное
    services.Configure<ApiSettings>(config.GetSection(ApiSettings.SectionName));

    return services.BuildServiceProvider();
}
```

---

## 🔗 Дальше читай

- 👉 [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) — обработка ошибок
- 👉 [10-BUILD-CI-CD.md](10-BUILD-CI-CD.md) — CI/CD и секреты
- 👉 [README.md](README.md) — навигация

---

**Помни: секреты в коде = взлом production.**