# 07 — WPF-специфика

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [05-PERFORMANCE.md](05-PERFORMANCE.md)

---

## 🧩 CommunityToolkit.Mvvm (source generators)

```csharp
public partial class UserViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsLoading = true;
        try
        {
            await _userService.SaveAsync(new User(Name));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Name) && !IsLoading;
}
```

---

## 🔄 Converters

```
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

---

## 📌 Binding Best Practices

```
<!-- ✅ OneWay для readonly -->
<TextBlock Text="{Binding DisplayName, Mode=OneWay}" />

<!-- ✅ UpdateSourceTrigger для мгновенной валидации -->
<TextBox Text="{Binding Email, UpdateSourceTrigger=PropertyChanged}" />

<!-- ✅ FallbackValue для graceful degradation -->
<TextBlock Text="{Binding OptionalField, FallbackValue='Нет данных'}" />
```

---

**Следующий файл:** [08-TESTING.md](https://08-TESTING.md)

