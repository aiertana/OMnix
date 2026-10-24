using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     卡片悬停时浮起来的投影。
///     这里用 XAML 层的 ThemeShadow，而不是 Composition 的 DropShadow：
///     DropShadow 的形状就是承载它的 SpriteVisual 的矩形，卡片有 16px 圆角，
///     四角会露出方方正正的直角阴影，比不加还难看。ThemeShadow 是 XAML
///     渲染管线自己画的，会跟着元素的圆角裁剪走。
/// </summary>
public static class ShadowFx
{
    /// <summary>挂上悬停投影</summary>
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(ShadowFx),
            new PropertyMetadata(false, OnAttachChanged));

    /// <summary>悬停时抬起的 Z 深度，越大阴影越散、离得越远</summary>
    public static readonly DependencyProperty DepthProperty =
        DependencyProperty.RegisterAttached("Depth", typeof(float), typeof(ShadowFx),
            new PropertyMetadata(16f));

    public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

    public static float GetDepth(DependencyObject obj) => (float)obj.GetValue(DepthProperty);

    public static void SetDepth(DependencyObject obj, float value) => obj.SetValue(DepthProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.PointerEntered -= OnPointerEntered;
        element.PointerExited -= OnPointerExited;

        if (e.NewValue is not true)
        {
            Clear(element);
            return;
        }

        element.PointerEntered += OnPointerEntered;
        element.PointerExited += OnPointerExited;
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 简易版不要阴影，和 GlassFx 的上浮保持一致
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        Raise((FrameworkElement)sender);
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        Clear((FrameworkElement)sender);

    private static void Raise(FrameworkElement element)
    {
        if (element.Shadow is null)
        {
            var shadow = new ThemeShadow();

            // ThemeShadow 必须告诉它阴影投在谁身上，不然它不画
            var receiver = FindReceiver(element);
            if (receiver is not null)
                shadow.Receivers.Add(receiver);

            element.Shadow = shadow;
        }

        // 有 Z 偏移才有投影；XY 保持 0，位移交给 GlassFx 的 RenderTransform，
        // 两边叠加不会打架
        element.Translation = new Vector3(0, 0, GetDepth(element));
    }

    private static void Clear(FrameworkElement element) => element.Translation = Vector3.Zero;

    /// <summary>往上找第一个容器当作承接阴影的背景</summary>
    private static UIElement? FindReceiver(FrameworkElement element)
    {
        DependencyObject node = element;

        while (VisualTreeHelper.GetParent(node) is { } parent)
        {
            if (parent is Panel panel)
                return panel;

            node = parent;
        }

        return null;
    }
}
