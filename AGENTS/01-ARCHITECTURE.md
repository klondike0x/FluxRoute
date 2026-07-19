# 🧱 01. Архитектура и принципы

---

## MVVM + DI (обязательно)

```
┌──────────────────────────────────────────────┐
│ Views (XAML)                                 │
│  ↓ Binding                                   │
│ ViewModels (CommunityToolkit.Mvvm)          │
│  ↓ Dependency Injection                      │
│ Services (бизнес-логика)                     │
│  ↓ DI                                        │
│ Repositories/Clients (данные, API)           │
└──────────────────────────────────────────────┘
```

### Строгие правила:

- ✅ ViewModel **НЕ знает** про View (никаких `MessageBox.Show` напрямую)
- ✅ View **НЕ содержит** бизнес-логику (только привязки, триггеры, стили)
- ✅ Service **НЕ знает** про UI (никаких Dispatcher.Invoke внутри)
- ✅ Все зависимости через **конструктор** (DI), никаких `new Service()`
- ✅ Интерфейсы для **всех** сервисов (`IXxxService`)

---

## Структура проектов

```
FluxRoute/                         # WPF UI (Views, ViewModels, App.xaml)
├── App.xaml.cs                    # DI контейнер, логирование
├── Views/                         # UserControl, Window (XAML + code-behind)
├── ViewModels/                    # Наследники ObservableObject
├── Controls/                      # Кастомные UI контролы
├── Converters/                    # IValueConverter
├── Behaviors/                     # Microsoft.Xaml.Behaviors
└── Styles/                        # ResourceDictionary

FluxRoute.Core/                    # Бизнес-логика
├── Models/                        # DTO, сущности
├── Services/                      # Интерфейсы + реализации
├── Configuration/                 # AppSettings классы
├── Results/                       # Result<T>, Error
├── Extensions/                    # Extension methods
├── Events/                        # Event aggregator, pub/sub
└── Helpers/                       # Утилиты

FluxRoute.AI/                      # AI движок
├── Sampling/                      # Thompson Sampling
├── Genetic/                       # Генетический алгоритм
└── Models/                        # AI-specific модели

FluxRoute.Updater/                 # Автообновление (независимо)

FluxRoute.Core.Tests/              # xUnit + Moq
├── Services/
├── ViewModels/
└── Helpers/
```

---

## Цепочка зависимостей (НЕ НАРУШАТЬ)

```
FluxRoute (UI)
    ↓
FluxRoute.AI
    ↓
FluxRoute.Core ← FluxRoute.Updater (параллельно)
```

⛔ **Циклические зависимости запрещены.**

---

## Что находится в каждом проекте?

### FluxRoute (UI)

**Views/** — XAML файлы:
- `MainWindow.xaml`
- `SettingsWindow.xaml`
- `UserControls/*.xaml`

**ViewModels/** — логика UI:
- `MainViewModel.cs` — привязана к `MainWindow`
- `SettingsViewModel.cs` — для `SettingsWindow`
- Наследуют `ObservableObject` из CommunityToolkit.Mvvm

**App.xaml.cs** — точка входа, DI контейнер:
```csharp
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();
    // регистрация всего
    return services.BuildServiceProvider();
}
```

---

### FluxRoute.Core (Бизнес-логика)

**Models/** — DTO и сущности:
```csharp
public class User
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
```

**Services/** — интерфейсы и реализации:
```csharp
public interface IUserService
{
    Task<Result<User>> GetUserAsync(int id, CancellationToken ct = default);
}

public class UserService : IUserService
{
    public async Task<Result<User>> GetUserAsync(int id, CancellationToken ct = default)
    {
        // реализация
    }
}
```

**Configuration/** — классы для `IOptions<T>`:
```csharp
public class ApiSettings
{
    public const string SectionName = "Api";
    public string BaseUrl { get; init; } = string.Empty;
}
```

**Results/** — `Result<T>` паттерн:
```csharp
public abstract record Result<T>
{
    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(string Error) : Result<T>;
}
```

---

### FluxRoute.AI (AI)

**Sampling/** — Thompson Sampling реализация

**Genetic/** — генетический алгоритм

**Models/** — AI-специфичные модели

---

### FluxRoute.Core.Tests (Тесты)

**Services/** — тесты для сервисов

**ViewModels/** — тесты для ViewModels

---

## DI в App.xaml.cs

```csharp
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Конфигурация
        services.Configure<ApiSettings>(config => {...});

        // Сервисы
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<ILogger<App>>();

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
```

---

## 🔗 Дальше читай

- 👉 [02-ASYNC.md](02-ASYNC.md) — асинхронность
- 👉 [06-DI-LIFECYCLE.md](06-DI-LIFECYCLE.md) — DI lifecycle
- 👉 [README.md](README.md) — навигация

---

**Помни: архитектура — основа стабильности проекта.**