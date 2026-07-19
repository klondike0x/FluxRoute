# 06 — Dependency Injection Lifecycle

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [01-ARCHITECTURE.md](01-ARCHITECTURE.md)

---

## 📋 Правила для Desktop-приложения

| Lifetime      | Когда использовать               | Пример                                            |
| ------------- | -------------------------------- | ------------------------------------------------- |
| **Singleton** | Stateless сервисы, кэши, фабрики | `ILogger`, `IHttpClientFactory`, `IConfiguration` |
| **Scoped**    | Ограничено логической операцией  | Не типично для desktop                            |
| **Transient** | Создавать каждый раз             | ViewModels, stateless helpers                     |

---

## 🔧 Типичная регистрация

```csharp
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();

    // Конфигурация
    services.Configure<ApiSettings>(Configuration.GetSection(ApiSettings.SectionName));

    // Логирование
    services.AddLogging(builder => builder.AddSerilog());

    // HTTP
    services.AddHttpClient("FluxApi", (sp, client) =>
    {
        var settings = sp.GetRequiredService<IOptions<ApiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    })
    .AddPolicyHandler(GetRetryPolicy());

    // Сервисы — Singleton
    services.AddSingleton<IDataService, DataService>();
    services.AddSingleton<IEventAggregator, EventAggregator>();

    // ViewModels — Transient
    services.AddTransient<MainViewModel>();
    services.AddTransient<SettingsViewModel>();

    // Views
    services.AddTransient<MainWindow>();
    services.AddTransient<SettingsWindow>();

    return services.BuildServiceProvider();
}
```

**Важно:** ViewModel, которая живёт всё время работы приложения, может быть Singleton.

---

**Следующий файл:** [07-WPF-GUIDELINES.md](https://07-WPF-GUIDELINES.md)

