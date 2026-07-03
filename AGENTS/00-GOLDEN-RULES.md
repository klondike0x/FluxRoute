# 🚨 00. Золотые правила

> **Читай это ПЕРВЫМ перед любой кодировкой**

---

## 10 критических правил FluxRoute

| #   | Правило                                           | Последствие нарушения  | Раздел |
| --- | ------------------------------------------------- | ---------------------- | --- |
| 1   | **НИКОГДА** не блокируй UI thread                 | Зависание приложения   | [02-ASYNC.md](02-ASYNC.md) |
| 2   | **ВСЕГДА** используй `async/await` для I/O        | Thread starvation      | [02-ASYNC.md](02-ASYNC.md) |
| 3   | **ВСЕГДА** логируй через `ILogger<T>`             | Слепота при дебаге     | [03-SECURITY.md](03-SECURITY.md) |
| 4   | **НИКОГДА** не хардкодь секреты                   | Утечка в Git           | [03-SECURITY.md](03-SECURITY.md) |
| 5   | **ВСЕГДА** пиши тесты для новой логики            | Регрессии              | [08-TESTING.md](08-TESTING.md) |
| 6   | **НИКОГДА** не создавай `new HttpClient()`        | Socket exhaustion      | [02-ASYNC.md](02-ASYNC.md) |
| 7   | **ВСЕГДА** используй DI для сервисов              | Несопровождаемый код   | [06-DI-LIFECYCLE.md](06-DI-LIFECYCLE.md) |
| 8   | **ВСЕГДА** возвращай `Result<T>` из сервисов      | Unhandled exceptions   | [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) |
| 9   | **НИКОГДА** не забывай `IDisposable`              | Memory leaks           | [05-PERFORMANCE.md](05-PERFORMANCE.md) |
| 10  | **ВСЕГДА** проверяй `IsNullOrEmpty` перед работой | NullReferenceException | [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) |

---

## ⚡ Быстрая справка

### ❌ ЗАПРЕЩЕНО (абсолютно)

```csharp
// 1. Синхронные блокирующие вызовы
var result = _service.GetDataAsync().Result;  // ❌ БЛОКИРУЕТ!
var result = _service.GetDataAsync().Wait();  // ❌ БЛОКИРУЕТ!

// 2. HttpClient в конструкторе
private readonly HttpClient _client = new();  // ❌ УТЕЧКА СОКЕТОВ!

// 3. Хардкод секретов
var apiKey = "sk-1234567890";                 // ❌ УТЕЧКА!

// 4. MessageBox в ViewModel
MessageBox.Show("Удалить?");                  // ❌ UI В VM!

// 5. Глушение исключений
try { await DoAsync(); } catch { }            // ❌ ТИХО ПАДАЕТ!

// 6. Забывание CancellationToken
await _service.GetDataAsync();                // ❌ НЕ ОТМЕНЯЕТСЯ!

// 7. Комментарии на английском
// This method loads data                    // ❌ НА РУССКОМ!
```

### ✅ ПРАВИЛЬНО (обязательно)

```csharp
// 1. Асинхронность
var result = await _service.GetDataAsync();   // ✅

// 2. HttpClientFactory
private readonly HttpClient _client;
public MyService(IHttpClientFactory factory) // ✅
{
    _client = factory.CreateClient("MyApi");
}

// 3. Конфигурация через IOptions
private readonly IOptions<ApiSettings> _options;
public MyService(IOptions<ApiSettings> options) // ✅
{
    _options = options;
}

// 4. Диалоги через сервис
private readonly IDialogService _dialog;
await _dialog.ConfirmAsync("Удалить?");      // ✅

// 5. Логирование ошибок
try { await DoAsync(); }
catch (Exception ex)
{
    _logger.LogError(ex, "Ошибка");          // ✅
    return Result.Fail("Не удалось", ex);
}

// 6. CancellationToken везде
public async Task<Result> DoAsync(           // ✅
    CancellationToken ct = default)

// 7. Комментарии на русском
// Этот метод загружает данные               // ✅
```

---

## 🎯 Золотое правило #1: UI thread

```csharp
// ❌ БУДЕТ ПАДАТЬ
private async Task LoadAsync()
{
    var data = await _service.GetDataAsync(); // из background thread
    MyListBox.Items.Add(data);                // InvalidOperationException!
}

// ✅ ПРАВИЛЬНО
private async Task LoadAsync()
{
    var data = await _service.GetDataAsync();
    // ViewModel обновляет ObservableCollection
    Items.Add(data); // привязка сама всё обновит
}
```

---

## 🎯 Золотое правило #2: Async/Await

```csharp
// ❌ НЕПРАВИЛЬНО
public void LoadData()         // void! + синхронно!
{
    var data = GetDataAsync().Result;  // блокирует!
}

// ✅ ПРАВИЛЬНО
public async Task LoadDataAsync()     // Task! + async!
{
    var data = await GetDataAsync();   // не блокирует
}
```

---

## 🎯 Золотое правило #3: HttpClient

```csharp
// ❌ ПЛОХО (Socket Exhaustion)
public class MyService
{
    public async Task<Data> GetAsync()
    {
        using var client = new HttpClient();  // УТЕЧКА!
        return await client.GetAsync(...);
    }
}

// ✅ ХОРОШО
public class MyService
{
    private readonly HttpClient _client;
    public MyService(IHttpClientFactory factory)
    {
        _client = factory.CreateClient("MyApi");
    }
    public async Task<Data> GetAsync()
    {
        return await _client.GetAsync(...);
    }
}
```

---

## 🎯 Золотое правило #4: Result<T>

```csharp
// ❌ ПЛОХО (exception throwing)
public async Task<Data> GetDataAsync()
{
    if (string.IsNullOrEmpty(url))
        throw new ArgumentException(...);  // исключение!
    // ...
}

// ✅ ХОРОШО (graceful degradation)
public async Task<Result<Data>> GetDataAsync()
{
    if (string.IsNullOrEmpty(url))
        return Result<Data>.Fail("URL пустой");
    // ...
    return Result<Data>.Ok(data);
}
```

---

## 🎯 Золотое правило #5: Логирование

```csharp
// ❌ ПЛОХО (логирует секреты)
_logger.LogInformation("API Key: {ApiKey}", settings.ApiKey);

// ✅ ХОРОШО (маскирует)
_logger.LogInformation("Используется API Key: {Key}",
    string.IsNullOrEmpty(settings.ApiKey) ? "<empty>" : "***");
```

---

## 📋 Чеклист перед кодировкой

- [ ] Прочитал все 10 правил выше
- [ ] Понял последствия для каждого
- [ ] Готов писать код с учётом этих правил

---

## 🔗 Дальше читай

- 👉 [14-QUICK-START.md](14-QUICK-START.md) — быстрый старт
- 👉 [09-COMMON-MISTAKES.md](09-COMMON-MISTAKES.md) — типичные ошибки
- 👉 [README.md](README.md) — навигация по всем файлам

---

**Помни: эти правила критичны. Нарушение → проблемы в production.**