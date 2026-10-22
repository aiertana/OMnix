using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     玻璃卡片的交互反馈（只挂在想要的元素上，不用改模板）。
///     参考的网页里那套是：hover 抬起来一点、按下缩一下、掠过一道光。
///     这里用 CompositeTransform 做同样的三件事，动画曲线用它的 cubic-bezier 手感。
///     简易版（MotionEnabled=false）时全部不触发，退回静态玻璃。
/// </summary>
public static class GlassFx
{
    /// <summary>挂上悬停 / 按压反馈</summary>
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(GlassFx),
            new PropertyMetadata(false, OnAttachChanged));

    /// <summary>悬停时抬起的像素，参考稿是 4</summary>
    public static readonly DependencyProperty LiftProperty =
        DependencyProperty.RegisterAttached("Lift", typeof(double), typeof(GlassFx),
            new PropertyMetadata(4.0));

    public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

    public static double GetLift(DependencyObject obj) => (double)obj.GetValue(LiftProperty);

    public static void SetLift(DependencyObject obj, double value) => obj.SetValue(LiftProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.PointerEntered -= OnPointerEntered;
        element.PointerExited -= OnPointerExited;
        element.PointerPressed -= OnPointerPressed;
        element.PointerReleased -= OnPointerReleased;
        element.PointerCanceled -= OnPointerCanceled;

        if (e.NewValue is not true)
            return;

        EnsureTransform(element);

        element.PointerEntered += OnPointerEntered;
        element.PointerExited += OnPointerExited;
        element.PointerPressed += OnPointerPressed;
        element.PointerReleased += OnPointerReleased;
        element.PointerCanceled += OnPointerCanceled;
    }

    /// <summary>
    ///     元素自己可能已经用了别的 RenderTransform，那就别抢它的，
    ///     没有才塞一个 CompositeTransform 进去。
    /// </summary>
    private static CompositeTransform? EnsureTransform(FrameworkElement element)
    {
        if (element.RenderTransform is CompositeTransform existing)
            return existing;

        if (element.RenderTransform is not null && element.RenderTransform is not MatrixTransform)
            return null;

        var transform = new CompositeTransform();
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        return transform;
    }

    // hover: translateY(-4px) scale(1.015) / active: translateY(-1px) scale(0.98)
    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        Animate((FrameworkElement)sender, 1.015, -GetLift((DependencyObject)sender), 260);

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        Animate((FrameworkElement)sender, 1.0, 0, 300);

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e) =>
        Animate((FrameworkElement)sender, 0.98, -1, 90);

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e) =>
        Animate((FrameworkElement)sender, 1.015, -GetLift((DependencyObject)sender), 180);

    private static void OnPointerCanceled(object sender, PointerRoutedEventArgs e) =>
        Animate((FrameworkElement)sender, 1.0, 0, 160);

    private static void Animate(FrameworkElement element, double scale, double offsetY, int milliseconds)
    {
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        if (element.RenderTransform is not CompositeTransform transform)
            return;

        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var storyboard = new Storyboard();

        storyboard.Children.Add(Make(transform, "ScaleX", scale, duration));
        storyboard.Children.Add(Make(transform, "ScaleY", scale, duration));
        storyboard.Children.Add(Make(transform, "TranslateY", offsetY, duration));

        storyboard.Begin();
    }

    private static DoubleAnimation Make(CompositeTransform transform, string property, double to, Duration duration)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }
}
