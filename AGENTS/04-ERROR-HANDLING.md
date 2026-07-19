# 🧩 04. Обработка ошибок

---

## Result<T> — стандартный паттерн

### Определение

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

---

## Использование в сервисах

### ❌ ПЛОХО (выбрасывает исключения)

```csharp
public class DataService
{
    public async Task<Data> GetDataAsync()
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentException("URL пустой");  // Exception!

        var response = await _client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(...);  // Exception!

        return await response.Content.ReadAsAsync<Data>();
    }
}
```

### ✅ ПРАВИЛЬНО (возвращает Result<T>)

```csharp
public class DataService : IDataService
{
    public async Task<Result<Data>> GetDataAsync(CancellationToken ct = default)
    {
        // Валидация входных данных
        if (string.IsNullOrEmpty(url))
            return Result<Data>.Fail("URL не настроен");

        try
        {
            var response = await _client.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Result<Data>.Fail($"HTTP {response.StatusCode}");

            var data = await response.Content
                .ReadFromJsonAsync<Data>(cancellationToken: ct)
                .ConfigureAwait(false);

            return Result<Data>.Ok(data!);
        }
        catch (OperationCanceledException)
        {
            return Result<Data>.Fail("Операция отменена");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке данных");
            return Result<Data>.Fail("Ошибка сети", ex);
        }
    }
}
```

---

## Graceful Degradation в ViewModel

### Правило: приложение НЕ падает, показываем ошибку в UI

```csharp
public partial class DataViewModel : ObservableObject
{
    private readonly IDataService _service;
    private readonly ILogger<DataViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<Data> items = new();

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isLoading;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var result = await _service.GetDataAsync();

            if (result.IsSuccess)
            {
                // Успех
                Items = new ObservableCollection<Data>(result.Value!);
                _logger.LogInformation("Загружено {Count} элементов", Items.Count);
            }
            else
            {
                // Ошибка, но приложение живо
                ErrorMessage = result.Error;
                _logger.LogWarning("Ошибка загрузки: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            // Это НЕ должно происходить, сервис должен возвращать Result.Fail
            _logger.LogError(ex, "Непредвиденная ошибка в LoadAsync");
            ErrorMessage = "Произошла непредвиденная ошибка. Попробуйте позже.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

### XAML для отображения ошибки

```xml
<Grid>
    <StackPanel>
        <!-- Список данных -->
        <ListBox ItemsSource="{Binding Items}" />

        <!-- Ошибка -->
        <TextBlock 
            Text="{Binding ErrorMessage}" 
            Foreground="Red"
            Visibility="{Binding ErrorMessage, Converter={StaticResource StringToVisibility}}" />

        <!-- Кнопка повтора -->
        <Button 
            Content="Повторить загрузку"
            Command="{Binding LoadCommand}"
            IsEnabled="{Binding IsLoading, Converter={StaticResource InvertBoolConverter}}" />
    </StackPanel>
</Grid>
```

---

## Цепочка обработки ошибок

```
Ошибка в сервисе
    ↓
  Result<T>.Fail(...)
    ↓
ViewModel проверяет IsSuccess
    ↓
Если false → ErrorMessage в UI
    ↓
Пользователь видит сообщение и может повторить
```

---

## Валидация и ошибки

```csharp
public record CreateUserRequest(string Name, string Email);

public class UserService : IUserService
{
    public async Task<Result<User>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        // Валидация
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<User>.Fail("Имя не может быть пустым");

        if (string.IsNullOrWhiteSpace(request.Email))
            return Result<User>.Fail("Email не может быть пустым");

        if (!request.Email.Contains("@"))
            return Result<User>.Fail("Email некорректен");

        // Бизнес-логика
        try
        {
            var user = new User { Name = request.Name, Email = request.Email };
            await _repository.SaveAsync(user, ct).ConfigureAwait(false);
            return Result<User>.Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании пользователя");
            return Result<User>.Fail("Не удалось создать пользователя", ex);
        }
    }
}
```

---

## Логирование ошибок

```csharp
public async Task<Result<Data>> GetDataAsync(CancellationToken ct = default)
{
    try
    {
        _logger.LogInformation("Начало загрузки данных");

        var response = await _client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var data = await response.Content
            .ReadFromJsonAsync<Data>(cancellationToken: ct)
            .ConfigureAwait(false);

        _logger.LogInformation("Успешно загружены данные");
        return Result<Data>.Ok(data!);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _logger.LogWarning(ex, "Данные не найдены (404)");
        return Result<Data>.Fail("Данные не найдены");
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
    {
        _logger.LogWarning(ex, "Ошибка аутентификации (401)");
        return Result<Data>.Fail("Ошибка аутентификации");
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Операция отменена пользователем");
        return Result<Data>.Fail("Операция отменена");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Непредвиденная ошибка при загрузке данных");
        return Result<Data>.Fail("Ошибка сети", ex);
    }
}
```

---

## 🔗 Дальше читай

- 👉 [02-ASYNC.md](02-ASYNC.md) — async/await
- 👉 [08-TESTING.md](08-TESTING.md) — тестирование ошибок
- 👉 [README.md](README.md) — навигация

---

**Помни: graceful degradation > crash и сообщение об ошибке в UI > молчаливый отказ.**