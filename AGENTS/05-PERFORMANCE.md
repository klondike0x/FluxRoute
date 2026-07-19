# 05 — Производительность (WPF)

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [07-WPF-GUIDELINES.md](07-WPF-GUIDELINES.md)

---

## 📊 UI Virtualization

```xml
<!-- ❌ ПЛОХО — все элементы в памяти -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemTemplate>...</ItemsControl.ItemTemplate>
</ItemsControl>

<!-- ✅ ХОРОШО — virtualization -->
<ListBox ItemsSource="{Binding Items}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling">
    <ListBox.ItemTemplate>...</ListBox.ItemTemplate>
</ListBox>
```

---

## 🔄 Avoid UI Thread Blocking

```
// ❌ ПЛОХО — блокирует UI
private void ProcessData()
{
    var heavyResult = HeavyComputation();
}

// ✅ ХОРОШО — в background
private async Task ProcessDataAsync()
{
    IsProcessing = true;
    try
    {
        var heavyResult = await Task.Run(() => HeavyComputation());
        Data = heavyResult;
    }
    finally
    {
        IsProcessing = false;
    }
}
```

---

## 💾 Memory Leaks — типичные проблемы

| Проблема | Решение |
|---|---|
| Подписка на события без отписки | Реализуй `IDisposable` |
| `ObservableCollection` растёт бесконечно | Ограничивай размер или пагинируй |
| `HttpClient` создаётся каждый раз | Используй `IHttpClientFactory` |

**Правильный IDisposable:**

```
public class MyViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    public MyViewModel(IEventAggregator events)
    {
        _subscription = events.GetEvent<DataChangedEvent>().Subscribe(OnDataChanged);
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

**Следующий файл:** [06-DI-LIFECYCLE.md](https://06-DI-LIFECYCLE.md)

