using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：主按钮（<c>OmniXAccentButtonStyle</c>）上的效果。
///
///     **按钮上已经没有任何"跑动"的东西了**：
///     表面扫光、外圈环形流光、顶部流光三版都试过，不是不动就是看不见，
///     已经按你的要求全部删掉。
///
///     现在按钮表面是一层"眼影珠光"：像手指蘸了银粉从左往右抹过去 ——
///     起手处最厚最亮，越往右越薄，边上化开没了；上沿带一道油光。
///     三层渐变都写在模板里（横向抹痕 + 起手的椭圆柔光 + 竖向光泽），
///     跟着按钮尺寸自己拉伸。
///     这里只负责让整层很慢地明暗起伏一下（<see cref="Breathe" />），像珠光在反光。
///
///     hover 抬 2px、按下缩 0.98 也归这里管：模板里的 <c>VisualStateManager</c> 实测整组不生效
///     （控件首次套模板时当前状态就是 Normal，没有"切状态"这个动作，VSM 不会去播它的 Storyboard），
///     所以改成挂指针事件、在代码里起 Storyboard。
/// </summary>
public static class AccentButtonFx
{
    /// <summary>
    ///     荧光粉层最暗 / 最亮的透明度。
    ///     这层是"蒙了一层粉"，不是"刷了一道白"，所以整体压在 0.35 上下，
    ///     起伏幅度只有 0.08 —— 再大就开始抢按钮本身的颜色了。
    /// </summary>
    private const double GlossDim = 0.32;

    private const double GlossBright = 0.4;

    /// <summary>明暗走一趟的时间，慢一点才像反光，不像闪烁</summary>
    private const double BreatheSeconds = 3.2;

    /// <summary>hover 抬高的像素：清单上写的是 2px</summary>
    private const double LiftPx = -2;

    /// <summary>按下缩到的比例，清单上写的是 0.98</summary>
    private const double PressScale = 0.98;

    private const int HoverMs = 150;

    /// <summary>按下要跟手，比 hover 快一倍</summary>
    private const int PressMs = 70;

    /// <summary>禁用时整颗按钮淡到多少</summary>
    private const double DisabledOpacity = 0.4;

    /// <summary>简易版（没有动效）直接跳过所有动画，静态银粉</summary>
    private static bool Still => ThemeStyleService.Instance is { MotionEnabled: false };

    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached("Attach", typeof(bool), typeof(AccentButtonFx),
            new PropertyMetadata(false, OnAttachChanged));

    /// <summary>银粉层的呼吸动画，重撒的时候要停掉旧的</summary>
    private static readonly DependencyProperty BreatheProperty =
        DependencyProperty.RegisterAttached("Breathe", typeof(Storyboard), typeof(AccentButtonFx),
            new PropertyMetadata(null));

    /// <summary>模板里要动的零件，查一次存起来，事件里就不用反复找</summary>
    private static readonly DependencyProperty PartsProperty =
        DependencyProperty.RegisterAttached("Parts", typeof(Parts), typeof(AccentButtonFx),
            new PropertyMetadata(null));

    /// <summary>正在跑的悬停 / 按下动画，下一次要先把上一次停掉</summary>
    private static readonly DependencyProperty FeedbackProperty =
        DependencyProperty.RegisterAttached("Feedback", typeof(Storyboard), typeof(AccentButtonFx),
            new PropertyMetadata(null));

    public static bool GetAttach(DependencyObject obj) => (bool)obj.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject obj, bool value) => obj.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
            return;

        button.Loaded -= OnLoaded;

        if (e.NewValue is not true)
            return;

        button.Loaded += OnLoaded;

        // 关键：扫描是"页面已经显示出来之后"才补扫到的，那时按钮早就 Loaded 完了。
        // 再挂 Loaded 事件永远等不到第二次，按钮就一直没效果 ——
        // 首页卡片里那些按钮就是这么漏掉的。已经 Loaded 的，直接现在起。
        if (button.IsLoaded)
            OnLoaded(button, new RoutedEventArgs());
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        // 单个按钮上出意外不能把整轮挂载带下水：
        // 之前有异常崩在首页第一个按钮上，MotionFx 的扫描循环被打断，
        // 全站按钮一个效果都没挂上。
        try
        {
            StartGloss(button);
            StartFeedback(button);
        }
        catch (Exception)
        {
            // 这一颗按钮没挂上就算了，别连累其它按钮
        }
    }

    // ========== 表面珠光 ==========

    /// <summary>
    ///     珠光那三层（横向抹痕 + 起手的柔光 + 竖向光泽）全部写在模板里，
    ///     会自动跟着按钮尺寸拉伸，不用按尺寸重算。
    ///     这里只让它慢慢明暗起伏一下，像珠光在反光。
    /// </summary>
    private static void StartGloss(Button button)
    {
        if (Find(button, "Gloss") is { } layer)
            Breathe(button, layer);
    }

    /// <summary>
    ///     整层珠光很慢地明暗起伏一遍（0.5 ↔ 0.62），像粉面在反光。
    ///     简易版不起这层动画，珠光保持静态。
    /// </summary>
    private static void Breathe(Button button, FrameworkElement layer)
    {
        if (Still)
            return;

        GetBreathe(button)?.Stop();

        var animation = new DoubleAnimation
        {
            From = GlossDim,
            To = GlossBright,
            Duration = new Duration(TimeSpan.FromSeconds(BreatheSeconds)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(animation, layer);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        SetBreathe(button, storyboard);
        storyboard.Begin();
    }

    // ========== hover / 按下 ==========

    /// <summary>
    ///     悬停抬 2px、按下缩 0.98、禁用变淡 —— 全部挂到指针事件上，自己在事件里起 Storyboard。
    ///     模板里 VisualState 不生效的原因见类注释。
    /// </summary>
    private static void StartFeedback(Button button)
    {
        if (Find(button, "LayoutRoot") is not FrameworkElement root)
            return;

        if (root.RenderTransform is not CompositeTransform shift)
            return;

        var parts = new Parts { Shift = shift };
        SetParts(button, parts);

        ApplyEnabled(button);

        button.PointerEntered += (_, _) =>
        {
            parts.Pressed = false;
            Rise(button, parts);
        };

        button.PointerPressed += (_, _) =>
        {
            parts.Pressed = true;
            Press(button, parts);
        };

        button.PointerReleased += (_, _) =>
        {
            parts.Pressed = false;
            Rise(button, parts);
        };

        button.PointerExited += (_, _) =>
        {
            parts.Pressed = false;
            Rest(button, parts);
        };

        button.PointerCaptureLost += (_, _) =>
        {
            parts.Pressed = false;
            Rest(button, parts);
        };

        button.IsEnabledChanged += (_, _) => ApplyEnabled(button);
    }

    /// <summary>hover：整颗往上抬</summary>
    private static void Rise(Button button, Parts parts)
    {
        Play(button, HoverMs,
            (parts.Shift, nameof(CompositeTransform.TranslateY), LiftPx),
            (parts.Shift, nameof(CompositeTransform.ScaleX), 1.0),
            (parts.Shift, nameof(CompositeTransform.ScaleY), 1.0));
    }

    /// <summary>按下：缩到 0.98，同时落回原位（跟着鼠标压下去的感觉）</summary>
    private static void Press(Button button, Parts parts)
    {
        Play(button, PressMs,
            (parts.Shift, nameof(CompositeTransform.TranslateY), 0.0),
            (parts.Shift, nameof(CompositeTransform.ScaleX), PressScale),
            (parts.Shift, nameof(CompositeTransform.ScaleY), PressScale));
    }

    /// <summary>指针离开：全部还原</summary>
    private static void Rest(Button button, Parts parts)
    {
        Play(button, HoverMs,
            (parts.Shift, nameof(CompositeTransform.TranslateY), 0.0),
            (parts.Shift, nameof(CompositeTransform.ScaleX), 1.0),
            (parts.Shift, nameof(CompositeTransform.ScaleY), 1.0));
    }

    /// <summary>禁用时整颗淡下去（原来这是 Disabled 那个视觉状态干的活）</summary>
    private static void ApplyEnabled(Button button)
    {
        if (Find(button, "LayoutRoot") is not FrameworkElement root)
            return;

        root.Opacity = button.IsEnabled ? 1 : DisabledOpacity;
    }

    private static void Play(Button button, int milliseconds,
        params (DependencyObject Target, string Property, double To)[] items)
    {
        // 简易版不动：位形保持原样（缩放 1、不抬起）
        if (Still)
            return;

        // 同一个目标属性上叠加第二个 Storyboard 会互相打架，起新的之前先停掉旧的
        GetFeedback(button)?.Stop();

        var storyboard = new Storyboard();

        foreach (var (target, property, to) in items)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds))
            };

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);

            storyboard.Children.Add(animation);
        }

        SetFeedback(button, storyboard);
        storyboard.Begin();
    }

    private static Parts? GetParts(DependencyObject obj) => (Parts?)obj.GetValue(PartsProperty);

    private static void SetParts(DependencyObject obj, Parts? value) => obj.SetValue(PartsProperty, value);

    private static Storyboard? GetFeedback(DependencyObject obj) => (Storyboard?)obj.GetValue(FeedbackProperty);

    private static void SetFeedback(DependencyObject obj, Storyboard? value) => obj.SetValue(FeedbackProperty, value);

    private static Storyboard? GetBreathe(DependencyObject obj) => (Storyboard?)obj.GetValue(BreatheProperty);

    private static void SetBreathe(DependencyObject obj, Storyboard? value) => obj.SetValue(BreatheProperty, value);

    /// <summary>模板里参与悬停动画的那几个对象</summary>
    private sealed class Parts
    {
        public required CompositeTransform Shift { get; init; }

        public bool Pressed { get; set; }
    }

    /// <summary>按名字在模板里找元素（模板内的元素不在页面的 NameScope 里，只能自己遍历）</summary>
    private static FrameworkElement? Find(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement element && element.Name == name)
                return element;

            if (Find(child, name) is { } found)
                return found;
        }

        return null;
    }
}
