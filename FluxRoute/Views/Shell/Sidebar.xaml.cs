using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxRoute.Views.Shell;

public partial class Sidebar : System.Windows.Controls.UserControl
{
    public Sidebar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Анимирует фиолетовую таблетку к активной вкладке.
    /// </summary>
    public void AnimateNavIndicator(int tabIndex, bool animate = true)
    {
        if (NavIndicatorTransform == null) return;

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
        const double AboutSlotY = 417.0;

        if (tabIndex == 7) return AboutSlotY;

        int visualIndex = tabIndex switch
        {
            6 => 6,
            8 => 7,
            _ => tabIndex
        };
        return visualIndex * SlotHeight;
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
}