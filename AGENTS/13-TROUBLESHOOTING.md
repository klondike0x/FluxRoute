# 13 — Диагностика проблем

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [02-ASYNC.md](02-ASYNC.md)

---

## 🐛 UI зависает

### Диагностика

```csharp
_logger.LogDebug("Начало {Method} в потоке {Thread}",
    nameof(MethodName), Thread.CurrentThread.ManagedThreadId);
```

### Решения

1. Проверь `.Result`, `.Wait()`, `GetAwaiter().GetResult()`
2. Проверь синхронные I/O операции
3. Проверь тяжёлые вычисления без `Task.Run`
4. Проверь Dispatcher.Invoke на длинных операциях

---

## 💾 Memory leak

### Инструменты

- Visual Studio Diagnostic Tools
- dotMemory
- `WeakEventManager` для событий

### Частые причины

1. Подписка на `static event` без отписки
2. ViewModel хранит ссылку на View
3. `ObservableCollection` растёт без лимита
4. `HttpClient` создаётся каждый раз

---

## 🌐 Сокеты исчерпаны

**Симптом:** `HttpRequestException: No connection could be made`

**Решение:** Используй только `IHttpClientFactory`, никогда не создавай `new HttpClient()` в цикле.

---

**Следующий файл:** [14-QUICK-START.md](https://14-QUICK-START.md)

