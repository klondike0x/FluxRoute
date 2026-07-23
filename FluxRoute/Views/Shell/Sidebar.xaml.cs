using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using TextBlock = System.Windows.Controls.TextBlock;

namespace FluxRoute.Views.Shell;

public partial class Sidebar : System.Windows.Controls.UserControl
{
    private static readonly Brush ActiveBackground =
        (Brush)new BrushConverter().ConvertFrom("#1A233A")!;
    private static readonly Brush ActiveForeground =
        (Brush)new BrushConverter().ConvertFrom("#DCE4F0")!;
    private static readonly Brush ActiveIcon =
        (Brush)new BrushConverter().ConvertFrom("#9966FF")!;
    private static readonly Brush InactiveIcon =
        (Brush)new BrushConverter().ConvertFrom("#71717A")!;

    public Sidebar()
    {
        InitializeComponent();
    }

    /// <summary>True — показывать текст рядом с иконками.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(Sidebar),
            new PropertyMetadata(false));

    /// <summary>
    /// Анимирует фиолетовую таблетку к активной вкладке.
    /// </summary>
    public void AnimateNavIndicator(int tabIndex, bool animate = true)
    {
        if (NavIndicatorTransform == null) return;

        UpdateActiveItem(tabIndex);

        double targetY = CalculateTargetY(tabIndex);

        if (!animate)
        {
            // Мгновенно перемещаем таблетку без анимации (для ресайза)
            NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, null);
            NavIndicatorTransform.Y = targetY;
            return;
        }

        // Плавная анимация (для клика по вкладке)
        var animation = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    /// <summary>Обновляет фон и цвет иконки выбранной вкладки.</summary>
    private void UpdateActiveItem(int tabIndex)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(this))
        {
            if (!int.TryParse(button.CommandParameter?.ToString(), out var index))
                continue;

            var isActive = index == tabIndex;
            button.Background = isActive ? ActiveBackground : Brushes.Transparent;

            foreach (var text in FindVisualChildren<TextBlock>(button))
            {
                if (text.FontFamily.Source == "Segoe MDL2 Assets")
                    text.Foreground = isActive ? ActiveIcon : InactiveIcon;
                else if (isActive)
                    text.Foreground = ActiveForeground;
            }
        }
    }

    private double CalculateTargetY(int tabIndex)
    {
        // Ищем кнопку по CommandParameter (в XAML они заданы как "0", "1", "2" и т.д.)
        var targetButton = FindVisualChild<System.Windows.Controls.Button>(this, b => b.CommandParameter?.ToString() == tabIndex.ToString());

        if (targetButton != null)
        {
            try
            {
                // Получаем точные координаты кнопки относительно корневого контейнера сайдбара
                var transform = targetButton.TransformToVisual(this);
                var point = transform.Transform(new System.Windows.Point(0, 0));

                // Центрируем таблетку: 
                // point.Y — верхняя граница кнопки.
                // Прибавляем половину высоты кнопки (чтобы попасть в центр) 
                // и вычитаем половину высоты таблетки (10.0, так как Height=20)
                return point.Y + (targetButton.ActualHeight / 2.0) - 30.0;
            }
            catch
            {
                // Если визуальное дерево ещё не готово (например, при самом первом старте)
                // используем старый фолбэк
            }
        }

        // Фолбэк-логика (если кнопка не найдена)
        const double SlotHeight = 44.0;
        const double SettingsSlotY = 285.0;
        if (tabIndex == 6) return SettingsSlotY;
        return tabIndex * SlotHeight;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && predicate(t)) return t;
            var result = FindVisualChild(child, predicate);
            if (result != null) return result;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}