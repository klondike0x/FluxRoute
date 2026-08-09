namespace FluxRoute.ViewModels;

/// <summary>
/// Элемент основной навигации FluxRoute 1.7.0.
/// </summary>
public sealed record MainNavigationItem(int Index, string Name);

/// <summary>
/// Единый контракт порядка и названий основных вкладок.
/// </summary>
public static class MainNavigation
{
    public static IReadOnlyList<MainNavigationItem> Items { get; } =
    [
        new(0, "ГЛАВНАЯ"),
        new(1, "КОМПОНЕНТЫ"),
        new(2, "ОРКЕСТРАТОР"),
        new(3, "ХОСТЛИСТЫ"),
        new(4, "МОДИФИКАЦИИ"),
        new(5, "ДИАГНОСТИКА"),
        new(6, "ЛОГИ"),
        new(7, "НАСТРОЙКИ")
    ];

    public static string GetName(int index) =>
        Items.FirstOrDefault(item => item.Index == index)?.Name ?? string.Empty;
}
