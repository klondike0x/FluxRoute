# 09 — Типичные ошибки AI

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [00-GOLDEN-RULES.md](00-GOLDEN-RULES.md), [02-ASYNC.md](02-ASYNC.md)

---

## ❌ Ошибка 1: Синхронный вызов async

```csharp
var result = _service.GetDataAsync().Result; // БЛОКИРУЕТ!
```

✅ **Правильно:** `var result = await _service.GetDataAsync();`

---

## ❌ Ошибка 2: HttpClient в конструкторе

```
private readonly HttpClient _client = new(); // УТЕЧКА!
```

✅ **Правильно:** `IHttpClientFactory.CreateClient("MyApi")`

---

## ❌ Ошибка 3: MessageBox в ViewModel

```
MessageBox.Show("Удалить?", "", MessageBoxButton.YesNo); // UI в VM!
```

✅ **Правильно:** через `IDialogService`:

```
public interface IDialogService
{
    Task<bool> ConfirmAsync(string message, string title);
}
```

---

## ❌ Ошибка 4: Забывают CancellationToken

```
await _service.GetDataAsync(); // нет отмены
```

✅ **Правильно:** `Task<Result<Data>> GetDataAsync(CancellationToken ct = default)`

---

## ❌ Ошибка 5: Свайпать исключения

```
try { await DoSomethingAsync(); }
catch { } // ТИХО ПАДАЕТ!
```

✅ **Правильно:**

```
try { await DoSomethingAsync(); }
catch (Exception ex)
{
    _logger.LogError(ex, "Ошибка");
    return Result.Fail("Операция не удалась");
}
```

---

## ❌ Ошибка 6: Логирование секретов

```
_logger.LogInformation("API Key: {ApiKey}", apiKey);
```

✅ **Правильно:** не логируй секреты или маскируй.

---

## ❌ Ошибка 7: Комментарии на английском

В этом проекте **все комментарии на русском**.

---

**Следующий файл:** [10-BUILD-CI-CD.md](https://10-BUILD-CI-CD.md)

