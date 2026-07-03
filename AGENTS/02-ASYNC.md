# ⚡ 02. Асинхронность (КРИТИЧНО)

---

## Жёсткие правила

| ❌ ЗАПРЕЩЕНО                                    | ✅ ПРАВИЛЬНО          |
| ----------------------------------------------- | --------------------- |
| `.Result`, `.Wait()`                            | `await`               |
| `.GetAwaiter().GetResult()`                     | `await`               |
| `void async` методы (кроме event handlers)      | `Task async` методы   |
| `Task.Run` для I/O bound                        | `await` для I/O       |
| Забывать `ConfigureAwait(false)` в library code | Использовать где надо |

---

## Базовое правило: Avoid blocking

### ❌ ПЛОХО

```csharp
public void LoadData()
{
    var data = _service.GetDataAsync().Result;  // БЛОКИРУЕТ UI!
    ProcessData(data);
}
```

### ✅ ПРАВИЛЬНО

```csharp
public async Task LoadDataAsync()
{
    var data = await _service.GetDataAsync();   // Не блокирует
    ProcessData(data);
}
```

---

## UI Thread и Dispatcher

### Правило

**Любой вызов UI контрола — только из UI thread.**

### ❌ ПЛОХО

```csharp
public async Task LoadDataAsync()
{
    var data = await _service.GetDataAsync();  // из background thread?
    MyListBox.Items.Add(data);  // InvalidOperationException!
}
```

### ✅ ХОРОШО (через ViewModel + Binding)

```csharp
// В ViewModel
public partial class DataViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Data> items = new();

    public async Task LoadAsync()
    {
        var result = await _service.GetDataAsync();
        if (result.IsSuccess)
        {
            Items = new ObservableCollection<Data>(result.Value!);
            // Binding автоматически всё обновит в UI thread
        }
    }
}
```

### Если нужен Dispatcher (редко)

```csharp
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

---

## CancellationToken (обязательно)

### В сервисах

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
        return Result<Data>.Success(
            await response.Content.ReadFromJsonAsync<Data>(ct));
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Операция отменена пользователем");
        return Result<Data>.Failure("Операция отменена");
    }
}
```

### В ViewModel

```csharp
public partial class DataViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;

    [RelayCommand]
    private async Task LoadAsync()
    {
        _loadCts?.Cancel();  // отменить предыдущую
        _loadCts = new CancellationTokenSource();

        IsLoading = true;
        try
        {
            var result = await _service.GetDataAsync(_loadCts.Token);
            if (result.IsSuccess)
            {
                Items = new ObservableCollection<Data>(result.Value!);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Загрузка отменена");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelLoad()
    {
        _loadCts?.Cancel();
    }
}
```

---

## Task.Run (только для CPU-bound)

### ❌ ПЛОХО (для I/O)

```csharp
private async Task LoadDataAsync()
{
    var data = await Task.Run(() => _service.GetDataAsync());
    // Task.Run для I/O-bound = излишне!
}
```

### ✅ ХОРОШО (прямой await)

```csharp
private async Task LoadDataAsync()
{
    var data = await _service.GetDataAsync();  // I/O сам асинхронен
}
```

### ✅ ПРАВИЛЬНЫЙ Task.Run (для тяжелых вычислений)

```csharp
private async Task ProcessDataAsync()
{
    IsProcessing = true;
    try
    {
        var result = await Task.Run(() => HeavyComputation());
        // Вычисление на thread pool, не блокирует UI
        ProcessedData = result;
    }
    finally
    {
        IsProcessing = false;
    }
}

private Data HeavyComputation()
{
    // Синхронное, но в отдельном потоке
    return new Data { /* результат */ };
}
```

---

## ConfigureAwait (в library code)

### ❌ ПЛОХО (может быть deadlock)

```csharp
// В FluxRoute.Core
public async Task<Data> GetDataAsync()
{
    var response = await _client.GetAsync(url);  // Может вернуться в UI thread!
    return await response.Content.ReadAsAsync<Data>();
}
```

### ✅ ХОРОШО

```csharp
// В FluxRoute.Core
public async Task<Data> GetDataAsync()
{
    var response = await _client.GetAsync(url).ConfigureAwait(false);
    return await response.Content.ReadAsAsync<Data>().ConfigureAwait(false);
}
```

### Правило

- **WPF UI код**: не нужен `ConfigureAwait(false)`
- **Library code** (Core, AI): `ConfigureAwait(false)` обязателен

---

## Пример: Полный async workflow

```csharp
// FluxRoute.Core/Services/IDataService.cs
public interface IDataService
{
    Task<Result<List<Data>>> GetDataAsync(CancellationToken ct = default);
}

// FluxRoute.Core/Services/DataService.cs
public class DataService : IDataService
{
    private readonly HttpClient _client;
    private readonly ILogger<DataService> _logger;

    public DataService(IHttpClientFactory factory, ILogger<DataService> logger)
    {
        _client = factory.CreateClient("FluxApi");
        _logger = logger;
    }

    public async Task<Result<List<Data>>> GetDataAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client
                .GetAsync("/api/data", ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var data = await response.Content
                .ReadFromJsonAsync<List<Data>>(cancellationToken: ct)
                .ConfigureAwait(false);

            _logger.LogInformation("Загружено {Count} элементов", data?.Count ?? 0);
            return Result<List<Data>>.Ok(data ?? new());
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Запрос отменён пользователем");
            return Result<List<Data>>.Fail("Операция отменена");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Ошибка при запросе к API");
            return Result<List<Data>>.Fail("Ошибка сети", ex);
        }
    }
}

// FluxRoute/ViewModels/DataViewModel.cs
public partial class DataViewModel : ObservableObject
{
    private readonly IDataService _service;
    private readonly ILogger<DataViewModel> _logger;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private ObservableCollection<Data> items = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public DataViewModel(IDataService service, ILogger<DataViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var result = await _service.GetDataAsync(_cts.Token);

            if (result.IsSuccess)
            {
                Items = new ObservableCollection<Data>(result.Value!);
            }
            else
            {
                ErrorMessage = result.Error;
                _logger.LogWarning("Ошибка загрузки: {Error}", result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Загрузка отменена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Непредвиденная ошибка");
            ErrorMessage = "Произошла ошибка";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CancelLoad()
    {
        _cts?.Cancel();
    }
}
```

---

## 🔗 Дальше читай

- 👉 [03-SECURITY.md](03-SECURITY.md) — конфигурация и безопасность
- 👉 [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) — обработка ошибок
- 👉 [README.md](README.md) — навигация

---

**Помни: async/await — основа responsive UI.**