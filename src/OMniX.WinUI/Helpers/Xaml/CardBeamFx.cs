using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     卡片 hover 时顶上冒出来的那条 2px 渐变条（AccentPrimary → 粉 → 蓝）。
///     执行指令 2.2 要求它"hover 时出现"，静态常亮的那种不算。
///
///     Border 只能有一个 Child，所以按两种情况处理：
///     - Child 本来就是 Grid：直接往里塞一个置顶的 Rectangle（和已有 XAML 写法一致）
///     - 不是 Grid：把原来的 Child 包进一个 Grid 再塞，顺手把 Grid.Row / Canvas.Left
///       这类附加属性搬到外壳上，布局不受影响
/// </summary>
public static class CardBeamFx
{
    /// <summary>渐变条高度，执行指令写的是 2px</summary>
    private const double BeamHeight = 2;

    private const int FadeMs = 250;

    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(CardBeamFx),
            new PropertyMetadata(false, OnAttachChanged));

    /// <summary>当前卡片身上的那条渐变条，没有就是 null</summary>
    private static readonly DependencyProperty BeamProperty =
        DependencyProperty.RegisterAttached("Beam", typeof(UIElement), typeof(CardBeamFx),
            new PropertyMetadata(null));

    public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

    private static UIElement? GetBeam(DependencyObject obj) => (UIElement?)obj.GetValue(BeamProperty);

    private static void SetBeam(DependencyObject obj, UIElement? value) => obj.SetValue(BeamProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
            return;

        border.Loaded -= OnLoaded;
        border.PointerEntered -= OnPointerEntered;
        border.PointerExited -= OnPointerExited;

        if (e.NewValue is not true)
        {
            RemoveBeam(border);
            return;
        }

        border.Loaded += OnLoaded;
        border.PointerEntered += OnPointerEntered;
        border.PointerExited += OnPointerExited;

        // 扫描发生在 LayoutUpdated 里，元素一般已经量过了；
        // 万一还没有，等 Loaded 再补
        if (GetBeam(border) is null)
            TryInsert(border);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border && GetBeam(border) is null)
            TryInsert(border);
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        if (sender is Border border && GetBeam(border) is { } beam)
            Fade(beam, 1);
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && GetBeam(border) is { } beam)
            Fade(beam, 0);
    }

    private static void TryInsert(Border border)
    {
        var beam = new Rectangle
        {
            Height = BeamHeight,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false,
            Opacity = 0,
            Fill = BeamBrush()
        };

        var child = border.Child;

        if (child is Grid grid)
        {
            Grid.SetRow(beam, 0);
            Grid.SetRowSpan(beam, Math.Max(1, grid.RowDefinitions.Count));

            if (grid.ColumnDefinitions.Count > 1)
                Grid.SetColumnSpan(beam, grid.ColumnDefinitions.Count);

            grid.Children.Add(beam);
        }
        else if (child is not null)
        {
            // 原来那层可能挂着 Grid.Row / Canvas.Left 之类，搬到外壳上去
            var wrapper = new Grid();
            CarryOver(child, wrapper);

            border.Child = null;
            wrapper.Children.Add(child);
            wrapper.Children.Add(beam);
            border.Child = wrapper;
        }
        else
        {
            border.Child = new Grid { Children = { beam } };
        }

        SetBeam(border, beam);
    }

    private static void RemoveBeam(Border border)
    {
        if (GetBeam(border) is not { } beam)
            return;

        SetBeam(border, null);

        if (beam is FrameworkElement { Parent: Panel panel })
            panel.Children.Remove(beam);
    }

    /// <summary>把定位用的附加属性从原元素搬到外壳，避免包一层就跑位</summary>
    private static void CarryOver(UIElement from, UIElement to)
    {
        if (from.GetValue(Grid.RowProperty) is int row && row != 0)
            to.SetValue(Grid.RowProperty, row);

        if (from.GetValue(Grid.RowSpanProperty) is int rowSpan && rowSpan != 1)
            to.SetValue(Grid.RowSpanProperty, rowSpan);

        if (from.GetValue(Grid.ColumnProperty) is int column && column != 0)
            to.SetValue(Grid.ColumnProperty, column);

        if (from.GetValue(Grid.ColumnSpanProperty) is int columnSpan && columnSpan != 1)
            to.SetValue(Grid.ColumnSpanProperty, columnSpan);

        if (from.GetValue(Canvas.ZIndexProperty) is int z && z != 0)
            to.SetValue(Canvas.ZIndexProperty, z);

        var left = Canvas.GetLeft(from);
        if (!double.IsNaN(left))
            Canvas.SetLeft(to, left);

        var top = Canvas.GetTop(from);
        if (!double.IsNaN(top))
            Canvas.SetTop(to, top);
    }

    /// <summary>
    ///     渐变条用主题里的 OmniXCardBeamBrush（AccentPrimary → 粉 → 蓝），
    ///     取不到就现场拼一个，不能因为缺资源让卡片变秃。
    /// </summary>
    private static Brush BeamBrush()
    {
        if (Application.Current.Resources.TryGetValue("OmniXCardBeamBrush", out var value)
            && value is Brush brush)
            return brush;

        var fallback = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        fallback.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x4C, 0x6D) });
        fallback.GradientStops.Add(new GradientStop { Offset = 0.5, Color = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xB3, 0xD1) });
        fallback.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0xFF, 0x7E, 0xC8, 0xFF) });

        return fallback;
    }

    private static void Fade(UIElement beam, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(FadeMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        Storyboard.SetTarget(animation, beam);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Begin();
    }
}
