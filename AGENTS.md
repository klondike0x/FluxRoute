# 🤖 AGENTS.md — FluxRoute

> **Инструкции для AI-агентов** (Hermes, DeepSeek, Claude, MiMo Code, Copilot)  
> **Проект:** FluxRoute (C# 13, .NET 10, WPF, CommunityToolkit.Mvvm)  
> **Лицензия:** GPLv3  
> **Версионирование:** SemVer  
> **Обновлено:** 2026-07-03  
> **Версия документа:** 2.0

---

## 🚨 ЗОЛОТЫЕ ПРАВИЛА (читать в первую очередь)

| #   | Правило                                           | Последствие нарушения  |
| --- | ------------------------------------------------- | ---------------------- |
| 1   | **НИКОГДА** не блокируй UI thread                 | Зависание приложения   |
| 2   | **ВСЕГДА** используй `async/await` для I/O        | Thread starvation      |
| 3   | **ВСЕГДА** логируй через `ILogger<T>`             | Слепота при дебаге     |
| 4   | **НИКОГДА** не хардкодь секреты                   | Утечка в Git           |
| 5   | **ВСЕГДА** пиши тесты для новой логики            | Регрессии              |
| 6   | **НИКОГДА** не создавай `new HttpClient()`        | Socket exhaustion      |
| 7   | **ВСЕГДА** используй DI для сервисов              | Несопровождаемый код   |
| 8   | **ВСЕГДА** возвращай `Result<T>` из сервисов      | Unhandled exceptions   |
| 9   | **НИКОГДА** не забывай `IDisposable`              | Memory leaks           |
| 10  | **ВСЕГДА** проверяй `IsNullOrEmpty` перед работой | NullReferenceException |

---

## 🧱 1. Архитектура и принципы

### 1.1 MVVM + DI (обязательно)

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

**Строгие правила:**

- ✅ ViewModel **НЕ знает** про View (никаких `MessageBox.Show` напрямую)
- ✅ View **НЕ содержит** бизнес-логику (только привязки, триггеры, стили)
- ✅ Service **НЕ знает** про UI (никаких Dispatcher.Invoke внутри)
- ✅ Все зависимости через **конструктор** (DI), никаких `new Service()`
- ✅ Интерфейсы для **всех** сервисов (`IXxxService`)

### 1.2 Структура проектов

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

**Цепочка зависимостей (НЕ НАРУШАТЬ):**

```
FluxRoute (UI)
    ↓
FluxRoute.AI
    ↓
FluxRoute.Core ← FluxRoute.Updater (параллельно)
```

⛔ **Циклические зависимости запрещены.**

---

## ⚡ 2. Асинхронность (КРИТИЧНО)

### 2.1 Жёсткие правила

| ❌ ЗАПРЕЩЕНО                                    | ✅ ПРАВИЛЬНО          |
| ----------------------------------------------- | --------------------- |
| `.Result`, `.Wait()`                            | `await`               |
| `.GetAwaiter().GetResult()`                     | `await`               |
| `void async` методы (кроме event handlers)      | `Task async` методы   |
| `Task.Run` для I/O bound                        | `await` для I/O       |
| Забывать `ConfigureAwait(false)` в library code | Использовать где надо |

### 2.2 UI Thread и Dispatcher

**Правило:** Любой вызов UI контрола — только из UI thread.

```csharp
// ❌ НЕПРАВИЛЬНО — InvalidOperationException
public async Task LoadDataAsync()
{
    var data = await _service.GetDataAsync();
    MyListBox.Items.Add(data); // может упасть, если вызвано из background thread
}

// ✅ ПРАВИЛЬНО — проверка и marshalling
public async Task LoadDataAsync()
{
    var data = await _service.GetDataAsync();

    if (Application.Current?.Dispatcher.CheckAccess() == true)
    {
        // Уже в UI thread
        MyListBox.Items.Add(data);
    }
    else
    {
        // Marshal в UI thread
        await Application.Current.Dispatcher.InvokeAsync(() =>
            MyListBox.Items.Add(data));
    }
}
```

**Идеальный подход — через ViewModel:** ViewModel обновляет `ObservableCollection`, а View автоматически реагирует через binding. **Никакого прямого обращения к UI из сервиса.**

### 2.3 Cancellation

**Обязательно** поддерживай `CancellationToken` во всех асинхронных методах с I/O:

```csharp
public interface IDataService
{
    Task<Result<Data>> GetDataAsync(CancellationToken ct = default);
}

public async Task<Result<Data>> GetDataAsync(CancellationToken ct = default)
{
    try
    {
        using var response = await _client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return Result<Data>.Success(await response.Content.ReadFromJsonAsync<Data>(ct));
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Операция отменена пользователем");
        return Result<Data>.Failure("Операция отменена");
    }
}
```

В ViewModel используй `CancellationTokenSource` для отмены долгих операций:

```csharp
private CancellationTokenSource? _loadCts;

public IAsyncRelayCommand CancelLoadCommand { get; }

public MainViewModel()
{
    CancelLoadCommand = new RelayCommand(() => _loadCts?.Cancel());
    LoadCommand = new AsyncRelayCommand(LoadAsync);
}

private async Task LoadAsync()
{
    _loadCts?.Cancel();
    _loadCts = new CancellationTokenSource();

    try
    {
        var result = await _service.GetDataAsync(_loadCts.Token);
        // обработка
    }
    catch (OperationCanceledException) { }
}
```

---

## 🛡️ 3. Безопасность и конфигурация

### 3.1 IOptions<T> — единственно верный путь

```csharp
// appsettings.json (коммитим БЕЗ секретов)
{
  "Api": {
    "BaseUrl": "https://api.fluxroute.io",
    "ApiKey": "",  // заполняется из env var
    "TimeoutSeconds": 30
  },
  "Serilog": { ... },
  "Updater": {
    "GitHubRepo": "klondike0x/FluxRoute"
  }
}

// FluxRoute.Core/Configuration/ApiSettings.cs
public class ApiSettings
{
    public const string SectionName = "Api";
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
}
```

**Валидация на старте:**

```csharp
// В App.xaml.cs — проверка критических настроек
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var apiSettings = Services.GetRequiredService<IOptions<ApiSettings>>().Value;

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

        base.OnStartup(e);
    }
}
```

### 3.2 Секреты в CI/CD

- Локально: `appsettings.Development.json` (в `.gitignore`)
- CI/CD: Environment variables или GitHub Secrets
- Никогда не логируй секреты:

```csharp
// ❌ ПЛОХО
_logger.LogInformation("API Key: {ApiKey}", settings.ApiKey);

// ✅ ХОРОШО
_logger.LogInformation("Используется API Key: {MaskedKey}",
    string.IsNullOrEmpty(settings.ApiKey) ? "<empty>" : "***masked***");
```

---

## 🧩 4. Паттерны обработки ошибок

### 4.1 Result<T> — стандарт в FluxRoute

```csharp
// FluxRoute.Core/Results/Result.cs
public abstract record Result
{
    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    public sealed record Success : Result
    {
        public static readonly Success Instance = new();
    }

    public sealed record Failure(string Error, Exception? Exception = null) : Result;

    public static Result Ok() => Success.Instance;
    public static Result Fail(string error, Exception? ex = null) => new Failure(error, ex);
}

public abstract record Result<T>
{
    public bool IsSuccess => this is Success;
    public T? Value => this is Success s ? s.Value : default;
    public string? Error => this is Failure f ? f.Error : null;

    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(string Error, Exception? Exception = null) : Result<T>;

    public static Result<T> Ok(T value) => new Success(value);
    public static Result<T> Fail(string error, Exception? ex = null) => new Failure(error, ex);
}
```

### 4.2 Graceful Degradation

```csharp
// В ViewModel — НИКОГДА не падаем, показываем ошибку в UI
private async Task LoadDataAsync()
{
    IsLoading = true;
    ErrorMessage = null;

    try
    {
        var result = await _dataService.GetDataAsync();

        if (result.IsSuccess)
        {
            Data = new ObservableCollection<DataItem>(result.Value!);
            _logger.LogInformation("Загружено {Count} элементов", Data.Count);
        }
        else
        {
            ErrorMessage = result.Error;
            _logger.LogWarning("Ошибка загрузки: {Error}", result.Error);
            // UI остаётся работоспособным, пользователь может повторить
        }
    }
    catch (Exception ex)
    {
        // Это НЕ должно происходить — сервис должен возвращать Result.Failure
        _logger.LogError(ex, "Непредвиденное исключение в LoadDataAsync");
        ErrorMessage = "Произошла непредвиденная ошибка. Попробуйте позже.";
    }
    finally
    {
        IsLoading = false;
    }
}
```

---

## 🚀 5. Performance (WPF-специфика)

### 5.1 UI Virtualization

**Обязательно** для длинных списков:

```xml
<!-- ❌ ПЛОХО — все элементы создаются в памяти -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemTemplate>...</ItemsControl.ItemTemplate>
</ItemsControl>

<!-- ✅ ХОРОШО — virtualization -->
<ListBox ItemsSource="{Binding Items}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         VirtualizingPanel.ScrollUnit="Pixel">
    <ListBox.ItemTemplate>...</ListBox.ItemTemplate>
</ListBox>
```

### 5.2 Avoid UI Thread Blocking

```csharp
// ❌ ПЛОХО — блокирует UI на время тяжёлой работы
private void ProcessData()
{
    var heavyResult = HeavyComputation(); // синхронно = лаг UI
}

// ✅ ХОРОШО — в background
private async Task ProcessDataAsync()
{
    IsProcessing = true;
    try
    {
        var heavyResult = await Task.Run(() => HeavyComputation());
        // Результат можно использовать в UI
        Data = heavyResult;
    }
    finally
    {
        IsProcessing = false;
    }
}
```

### 5.3 Memory Leaks — типичные WPF проблемы

| Проблема                                 | Решение                              |
| ---------------------------------------- | ------------------------------------ |
| Подписка на события без отписки          | Реализуй `IDisposable` и отписывайся |
| Замыкание на View из ViewModel           | Никогда не храни ссылку на View      |
| `ObservableCollection` растёт бесконечно | Ограничивай размер или пагинируй     |
| `HttpClient` создаётся каждый раз        | Используй `IHttpClientFactory`       |
| Статические события (`static event`)     | Weak events (`WeakEventManager`)     |

**Правильный `IDisposable`:**

```csharp
public class MyViewModel : ObservableObject, IDisposable
{
    private readonly IDataService _service;
    private readonly IDisposable _subscription;
    private bool _disposed;

    public MyViewModel(IDataService service, IEventAggregator events)
    {
        _service = service;
        _subscription = events.GetEvent<DataChangedEvent>()
                              .Subscribe(OnDataChanged);
    }

    private void OnDataChanged(DataChangedPayload payload)
    {
        // Обработка
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

---

## ⚙️ 6. Dependency Injection Lifecycle

### 6.1 Правила для Desktop-приложения

| Lifetime      | Когда использовать               | Пример                                            |
| ------------- | -------------------------------- | ------------------------------------------------- |
| **Singleton** | Stateless сервисы, кэши, фабрики | `ILogger`, `IHttpClientFactory`, `IConfiguration` |
| **Scoped**    | Ограничено логической операцией  | Не типично для desktop, использовать редко        |
| **Transient** | Создавать каждый раз при резолве | ViewModels, stateless helpers                     |

### 6.2 Типичная регистрация

```csharp
// App.xaml.cs
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();

    // Конфигурация
    services.Configure<ApiSettings>(Configuration.GetSection(ApiSettings.SectionName));
    services.Configure<UpdaterSettings>(Configuration.GetSection(UpdaterSettings.SectionName));

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

    // Сервисы — Singleton (stateless)
    services.AddSingleton<IDataService, DataService>();
    services.AddSingleton<ICacheService, MemoryCacheService>();
    services.AddSingleton<IEventAggregator, EventAggregator>();

    // ViewModels — Transient (новые для каждого окна)
    services.AddTransient<MainViewModel>();
    services.AddTransient<SettingsViewModel>();
    services.AddTransient<DataViewModel>();

    // Views
    services.AddTransient<MainWindow>();
    services.AddTransient<SettingsWindow>();

    return services.BuildServiceProvider();
}
```

**Важно:** ViewModel, которая живёт всё время работы приложения, может быть Singleton. Короткоживущие (окна настроек) — Transient.

---

## 🖥️ 7. WPF-Specific Guidelines

### 7.1 CommunityToolkit.Mvvm

Используй **source generators** для минимизации boilerplate:

```csharp
public partial class UserViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsLoading = true;
        try
        {
            await _userService.SaveAsync(new User(Name, Email));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !IsLoading;
}
```

### 7.2 Converters

Всегда реализуй `IValueConverter` для специфичной логики отображения:

```csharp
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var invert = parameter as string == "Invert";
            return (invert ? !b : b) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Регистрируй в `App.xaml`:

```xml
<Application.Resources>
    <converters:BoolToVisibilityConverter x:Key="BoolToVisibility" />
</Application.Resources>
```

### 7.3 Binding Best Practices

```xml
<!-- ✅ Mode=OneWay для readonly свойств -->
<TextBlock Text="{Binding DisplayName, Mode=OneWay}" />

<!-- ✅ UpdateSourceTrigger=PropertyChanged для мгновенной валидации -->
<TextBox Text="{Binding Email, UpdateSourceTrigger=PropertyChanged}" />

<!-- ✅ FallbackValue для graceful degradation -->
<TextBlock Text="{Binding OptionalField, FallbackValue='Нет данных'}" />

<!-- ✅ TargetNullValue для null значений -->
<Image Source="{Binding AvatarUrl, TargetNullValue='/Assets/default.png'}" />
```

---

## 🧪 8. Тестирование (xUnit + Moq)

### 8.1 Обязательный минимум

- **≥80% покрытие** для новой бизнес-логики
- **Каждый public метод** сервиса должен иметь минимум 3 теста: success, failure, edge case
- **ViewModels** тестируем через подмену сервисов

### 8.2 Паттерн Arrange-Act-Assert

```csharp
public class DataServiceTests
{
    private readonly Mock<IHttpClientFactory> _clientFactoryMock;
    private readonly Mock<IOptions<ApiSettings>> _optionsMock;
    private readonly Mock<ILogger<DataService>> _loggerMock;
    private readonly DataService _service;

    public DataServiceTests()
    {
        _clientFactoryMock = new Mock<IHttpClientFactory>();
        _optionsMock = new Mock<IOptions<ApiSettings>>();
        _loggerMock = new Mock<ILogger<DataService>>();

        _optionsMock.Setup(x => x.Value)
            .Returns(new ApiSettings
            {
                BaseUrl = "https://api.test.com",
                ApiKey = "test-key"
            });

        _service = new DataService(
            _clientFactoryMock.Object,
            _optionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetDataAsync_ValidResponse_ReturnsSuccess()
    {
        // Arrange
        var expectedData = new Data { Id = 1, Name = "Test" };
        var mockHandler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedData)
            });

        var httpClient = new HttpClient(mockHandler);
        _clientFactoryMock.Setup(x => x.CreateClient("FluxApi"))
            .Returns(httpClient);

        // Act
        var result = await _service.GetDataAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedData);
    }

    [Fact]
    public async Task GetDataAsync_HttpError_ReturnsFailure()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _clientFactoryMock.Setup(x => x.CreateClient("FluxApi"))
            .Returns(new HttpClient(mockHandler));

        // Act
        var result = await _service.GetDataAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("HTTP");
    }

    [Fact]
    public async Task GetDataAsync_Cancellation_ReturnsFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.GetDataAsync(cts.Token);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("отмен");
    }
}
```

### 8.3 Тестирование ViewModel

```csharp
public class UserViewModelTests
{
    [Fact]
    public void SaveCommand_CanExecute_FalseWhenNameEmpty()
    {
        var serviceMock = new Mock<IUserService>();
        var vm = new UserViewModel(serviceMock.Object);

        vm.Name = "";
        vm.Email = "test@example.com";

        vm.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SaveCommand_Success_SetsIsLoadingFalse()
    {
        var serviceMock = new Mock<IUserService>();
        serviceMock.Setup(x => x.SaveAsync(It.IsAny<User>()))
            .ReturnsAsync(Result.Ok());

        var vm = new UserViewModel(serviceMock.Object)
        {
            Name = "John",
            Email = "john@example.com"
        };

        await vm.SaveCommand.ExecuteAsync(null);

        vm.IsLoading.Should().BeFalse();
    }
}
```

---

## 🐛 9. Типичные ошибки AI-агентов (ОБЯЗАТЕЛЬНО)

### ❌ Ошибка 1: Синхронный вызов async

```csharp
// AI часто так пишет:
var result = _service.GetDataAsync().Result; // БЛОКИРУЕТ!

// Правильно:
var result = await _service.GetDataAsync();
```

### ❌ Ошибка 2: HttpClient в конструкторе

```csharp
// AI создаёт HttpClient напрямую:
public class MyService
{
    private readonly HttpClient _client = new(); // УТЕЧКА СОКЕТО��!
}

// Правильно:
public class MyService
{
    private readonly HttpClient _client;
    public MyService(IHttpClientFactory factory)
    {
        _client = factory.CreateClient("MyApi");
    }
}
```

### ❌ Ошибка 3: MessageBox в ViewModel

```csharp
// AI часто делает так:
private async Task DeleteAsync()
{
    var result = MessageBox.Show("Удалить?", "", MessageBoxButton.YesNo); // UI в VM!
}

// Правильно: через диалоговый сервис
public interface IDialogService
{
    Task<bool> ConfirmAsync(string message, string title);
}
```

### ❌ Ошибка 4: Забывают CancellationToken

```csharp
// AI часто:
await _service.GetDataAsync(); // нет отмены

// Правильно:
public Task<Result<Data>> GetDataAsync(CancellationToken ct = default);
```

### ❌ Ошибка 5: Свайпать исключения

```csharp
// AI пишет:
try { await DoSomethingAsync(); }
catch { } // ТИХО ПАДАЕТ!

// Правильно:
try { await DoSomethingAsync(); }
catch (Exception ex)
{
    _logger.LogError(ex, "Ошибка в DoSomethingAsync");
    return Result.Fail("Операция не удалась");
}
```

### ❌ Ошибка 6: Логирование секретов

```csharp
// AI логирует всё подряд:
_logger.LogInformation("Запрос: {ApiKey} -> {Url}", apiKey, url);

// Правильно:
_logger.LogInformation("Запрос к {Url}", url);
```

### ❌ Ошибка 7: Комментарии на английском

В этом проекте **все комментарии на русском** для консистентности.

---

## 🛠️ 10. Сборка, тесты, релиз

### 10.1 Локально

```bash
# Восстановление
dotnet restore FluxRoute.slnx

# Сборка
dotnet build FluxRoute.slnx

# Тесты (ВСЕГДА перед коммитом)
dotnet test FluxRoute.slnx --verbosity normal

# Форматирование
dotnet format FluxRoute.slnx

# Публикация
dotnet publish FluxRoute/FluxRoute.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish
```

### 10.2 CI/CD (GitHub Actions)

Триггеры:

- `workflow_dispatch` — ручной
- `push v*` — автоматический релиз

**Критическое правило:** тег `v1.2.3` ↔ `<Version>1.2.3</Version>` в csproj. Иначе билд падает.

---

## 📝 11. Git и Conventional Commits

### Формат:

```
<type>(<scope>): <описание на русском>

[опциональное тело]

[опциональные footer]
```

### Типы:

| Тип        | Когда использовать                      |
| ---------- | --------------------------------------- |
| `feat`     | Новая функциональность                  |
| `fix`      | Исправление бага                        |
| `docs`     | Документация                            |
| `style`    | Форматирование, пробелы                 |
| `refactor` | Рефакторинг без изменения поведения     |
| `perf`     | Оптимизация производительности          |
| `test`     | Добавление тестов                       |
| `build`    | Изменения в системе сборки              |
| `ci`       | CI/CD конфигурация                      |
| `chore`    | Мелкие задачи (обновление зависимостей) |

### Примеры:

```bash
feat(core): добавлен сервис загрузки конфигурации
fix(ui): исправлен лаг при загрузке списка
perf(ai): оптимизирован Thompson Sampling на 40%
test(services): добавлены тесты для DataService
docs: обновлён README с инструкциями
refactor(core): вынес валидацию в отдельный класс
```

### Breaking changes:

```bash
feat(api)!: изменил формат ответа GetData

breaking-change: поле Id переименовано в Identifier
```

---

## ✅ 12. Чеклист перед коммитом

**Используй этот список перед КАЖДЫМ коммитом:**

### Код:

- [ ] `dotnet test` — все тесты зелёные
- [ ] `dotnet format` — код отформатирован
- [ ] Покрытие новых модулей ≥80%
- [ ] Нет `.Result`, `.Wait()`, синхронных вызовов async
- [ ] Нет `new HttpClient()` — только через `IHttpClientFactory`
- [ ] Все async методы принимают `CancellationToken`
- [ ] Все public методы сервисов возвращают `Result<T>`
- [ ] Реализован `IDisposable` где надо
- [ ] Нет memory leaks (подписки на события, замыкания)

### Безопасность:

- [ ] Нет хардкода паролей, токенов, API-ключей
- [ ] Секреты только через `IOptions<T>`
- [ ] Логи не содержат конфиденциальных данных
- [ ] `appsettings.json` без реальных секретов

### Архитектура:

- [ ] Нет циклических зависимостей
- [ ] ViewModel не содержит прямых обращений к View
- [ ] Сервисы не знают про UI
- [ ] Все зависимости через DI
- [ ] Все сервисы имеют интерфейсы

### Качество:

- [ ] Логирование через `ILogger<T>` с контекстом
- [ ] Комментарии на русском
- [ ] Нет незаполненных TODO без Issue
- [ ] Имена методов/классов осмысленные

### Git:

- [ ] Коммит в формате Conventional Commits
- [ ] CHANGELOG обновлён (если это фича/фикс)
- [ ] Если релиз — версия в csproj соответствует тегу

---

## 🆘 13. Troubleshooting Guide

### Проблема: UI зависает

**Диагностика:**

```csharp
// Добавь в начало метода
_logger.LogDebug("Начало {Method} в потоке {Thread}",
    nameof(MethodName), Thread.CurrentThread.ManagedThreadId);
```

**Решения:**

1. Проверь `.Result`, `.Wait()`, `GetAwaiter().GetResult()`
2. Проверь синхронные I/O операции (`File.ReadAllText` вместо `File.ReadAllTextAsync`)
3. Проверь тяжёлые вычисления без `Task.Run`
4. Проверь Dispatcher.Invoke на длинных операциях

### Проблема: Memory leak

**Инструменты:**

- Visual Studio Diagnostic Tools
- dotMemory
- `WeakEventManager` для событий

**Частые причины:**

1. Подписка на `static event` без отписки
2. ViewModel хранит ссылку на View
3. `ObservableCollection` растёт без лимита
4. `HttpClient` создаётся каждый раз

### Проблема: Сокеты исчерпаны

**Симптом:** `HttpRequestException: No connection could be made`

**Решение:** Используй только `IHttpClientFactory`, никогда не создавай `new HttpClient()` в цикле.

---

## 📚 14. Ресурсы

### Официальная документация:

- [WPF Docs](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Dependency Injection](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
- [Polly](https://github.com/App-vNext/Polly)
- [Serilog](https://serilog.net/)

### Стиль:

- [Conventional Commits](https://www.conventionalcommits.org/)
- [SemVer](https://semver.org/)

### Инструменты:

- [AsyncFixer](https://github.com/pflueras/AsyncFixer) — анализатор async кода
- [SonarLint](https://www.sonarsource.com/products/sonarlint/) — анализ качества
- [ReSharper](https://www.jetbrains.com/resharper/) — рефакторинг

---

## 🎯 15. Быстрый старт для нового AI-агента

### При первом подключении:

1. Прочитай этот файл **полностью**
2. Изучи структуру проекта: `FluxRoute/`, `FluxRoute.Core/`, `FluxRoute.AI/`
3. Посмотри примеры в `FluxRoute.Core.Tests/`
4. Проверь `appsettings.json` для понимания конфигурации

### При получении задачи:

1. **Пойми** задачу (задай уточняющие вопросы)
2. **Проверь** архитектуру (куда вписать?)
3. **Спроектируй** интерфейсы (IService)
4. **Реализуй** с логированием и async
5. **Напиши** тесты (≥80% покрытия)
6. **Интегрируй** в DI
7. **Закоммити** по Conventional Commits
8. **Обнови** CHANGELOG

### Перед отправкой ответа:

- [ ] Код на C# 13, .NET 10
- [ ] Все правила из "Золотых правил" соблюдены
- [ ] Есть тесты
- [ ] Есть логирование
- [ ] Нет хардкода
- [ ] Комментарии на русском

---

## 📞 Контакты

- **Репозиторий:** https://github.com/klondike0x/FluxRoute
- **Issue tracker:** GitHub Issues
- **CI/CD:** `.github/workflows/`
- **Лицензия:** GPLv3 (см. `LICENSE`)

---

**Последнее обновление:** 2026-07-03  
**Версия:** 2.0  
**Для:** Hermes, DeepSeek, Claude, MiMo Code и других AI-агентов
