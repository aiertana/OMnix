using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OMniX.WinUI.Services;
using Windows.Foundation;
using Windows.UI;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：点击时的粒子飞散，照抄参考网页的 spawnParticles / spawnCloudRipple。
///
///     cloud-ripple：160x160 的径向渐变圆，scale 0 → 2、opacity 0.7 → 0，0.45s
///     particle    ：10 颗 6~20px 的圆点，粉 / 紫 / 蓝 / 白随机，
///                   往 (±40, -65~15) 的方向散开，scale 1 → 0.25、opacity 1 → 0，0.9s
///
///     画在一个铺满窗口、不挡点击的 Canvas 上，动画结束就把元素摘掉。
/// </summary>
public sealed class ClickFx
{
    // 参考网页是 16 颗。点击是离散动作，不像移动那样持续触发，多一点没关系
    private const int ParticleCount = 16;

    // 执行指令的四色：主色 #E84C6D / 淡粉 / 浅蓝 / 白
    private static readonly Color[] ParticleColors =
    [
        Color.FromArgb(0xFF, 0xE8, 0x4C, 0x6D),
        Color.FromArgb(0xFF, 0xFF, 0xB3, 0xD1),
        Color.FromArgb(0xFF, 0x7E, 0xC8, 0xFF),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
    ];

    private readonly Panel _layer;
    private readonly Random _random = new();

    public ClickFx(Panel layer)
    {
        _layer = layer;
    }

    public void Burst(Point position)
    {
        // 简易版不动
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        SpawnRipple(position);

        for (var i = 0; i < ParticleCount; i++)
            SpawnParticle(position);
    }

    /// <summary>
    ///     鼠标移动拖出来的小尾巴。刻意做得比点击粒子少很多：
    ///     移动是持续触发的，太密会一直在造元素和动画，稳态控制在 20 颗以内。
    /// </summary>
    public void Trail(Point position)
    {
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        // 执行指令：每次 2-3 个
        var count = 2 + _random.Next(2);

        for (var i = 0; i < count; i++)
            // 文档：4-12px、800ms、透明度 0.8
            SpawnParticle(position, spread: 24, seconds: 0.8, minSize: 4, maxSize: 12,
                fromOpacity: 0.8, drift: -6);
    }

    /// <summary>鼠标刚移到交互控件上，往四周散一小撮</summary>
    public void Hover(Point position)
    {
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        for (var i = 0; i < 10; i++)
            SpawnParticle(position, spread: 80, seconds: 0.7, minSize: 4, maxSize: 11,
                fromOpacity: 0.8, drift: -10);
    }

    /// <summary>
    ///     点击处先炸开一团雾。执行指令 4：从点击位置扩散 160px 的圆形波纹，
    ///     AccentPrimary 20% 不透明度，500ms 消失 —— 所以这里取当前主色再套透明度，
    ///     配色换了波纹也跟着换。
    /// </summary>
    private void SpawnRipple(Point position)
    {
        const double size = 160;

        var accent = RippleColor();

        var ripple = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
            Opacity = 0.7,
            RenderTransform = new ScaleTransform { ScaleX = 0, ScaleY = 0 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    // 20% → 10% → 透明
                    new GradientStop { Color = accent, Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0x1A, accent.R, accent.G, accent.B), Offset = 0.45 },
                    new GradientStop { Color = Color.FromArgb(0x00, accent.R, accent.G, accent.B), Offset = 0.7 }
                }
            }
        };

        Canvas.SetLeft(ripple, position.X - size / 2);
        Canvas.SetTop(ripple, position.Y - size / 2);
        _layer.Children.Add(ripple);

        var storyboard = new Storyboard();

        var transform = (ScaleTransform)ripple.RenderTransform;

        storyboard.Children.Add(Build(transform, nameof(ScaleTransform.ScaleX), 0, 2, 0.5));
        storyboard.Children.Add(Build(transform, nameof(ScaleTransform.ScaleY), 0, 2, 0.5));
        storyboard.Children.Add(Build(ripple, "Opacity", 0.7, 0, 0.5));

        storyboard.Completed += (_, _) => _layer.Children.Remove(ripple);
        storyboard.Begin();
    }

    /// <summary>当前主色，套上 20% 不透明度；取不到就用配色1 的珊瑚红</summary>
    private static Color RippleColor()
    {
        var color = Color.FromArgb(0xFF, 0xE8, 0x4C, 0x6D);

        // OmniXAccentColor 在 ThemeDictionaries 里，顶层索引取不到，走引用它的画刷
        if (Application.Current.Resources.TryGetValue("OmniXAccentBrush", out var value)
            && value is SolidColorBrush brush)
            color = brush.Color;

        return Color.FromArgb(0x33, color.R, color.G, color.B);
    }

    /// <summary>
    ///     一颗往外飞的小圆点。spread / seconds / drift 这几个参数是为了
    ///     让点击、拖尾、悬停三种场合共用同一套画法，只是规模和时长不同。
    /// </summary>
    private void SpawnParticle(Point position, double spread = 120, double seconds = 0.9,
        double minSize = 6, double maxSize = 20, double fromOpacity = 1.0, double drift = -25)
    {
        var size = minSize + _random.NextDouble() * (maxSize - minSize);
        var color = ParticleColors[_random.Next(ParticleColors.Length)];

        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
            Fill = new SolidColorBrush(color),
            RenderTransform = new CompositeTransform { CenterX = size / 2, CenterY = size / 2 }
        };

        Canvas.SetLeft(dot, position.X - size / 2);
        Canvas.SetTop(dot, position.Y - size / 2);
        _layer.Children.Add(dot);

        // 参考稿：tx = (±40)，ty = (±40) - 25，整体略偏上
        var tx = (_random.NextDouble() - 0.5) * spread;
        var ty = (_random.NextDouble() - 0.5) * spread + drift;

        var transform = (CompositeTransform)dot.RenderTransform;
        var storyboard = new Storyboard();

        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.TranslateX), 0, tx, seconds));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.TranslateY), 0, ty, seconds));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.ScaleX), 1, 0.25, seconds));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.ScaleY), 1, 0.25, seconds));
        storyboard.Children.Add(Build(dot, "Opacity", fromOpacity, 0.0, seconds));

        storyboard.Completed += (_, _) => _layer.Children.Remove(dot);
        storyboard.Begin();
    }

    private static DoubleAnimation Build(DependencyObject target, string property,
        double from, double to, double seconds)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }
}
