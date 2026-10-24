using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：卡片 hover 的一整套反馈，统一成一个开关。
///
///     这三件事以前是拆开挂的（MotionFx.ScanTree 里分别调 GlassFx / ShadowFx），
///     漏一个就少一样效果，而且卡片顶上那条渐变条（CardBeamFx）只认 Border，
///     混在扫描逻辑里很容易忘。现在收成一个 Attach：
///
///     - GlassFx    ：hover 抬起来、按下去回弹
///     - ShadowFx   ：hover 时阴影变大
///     - CardBeamFx ：hover 时顶上冒出 2px 的 红 → 粉 → 蓝 渐变条（只有 Border 才有）
///
///     三个类各自都认 ThemeStyleService.MotionEnabled，简易版下自己就不动了，
///     这里不用再判断一次。
/// </summary>
public static class CardHoverFx
{
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(CardHoverFx),
            new PropertyMetadata(false, OnAttachChanged));

    public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var on = e.NewValue is true;

        if (d is FrameworkElement element)
        {
            GlassFx.SetAttach(element, on);
            ShadowFx.SetAttach(element, on);
        }

        // 渐变条要往 Border 里塞子元素的，非 Border 挂不上，静默跳过就行
        if (d is Border border)
            CardBeamFx.SetAttach(border, on);
    }
}
