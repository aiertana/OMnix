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
    private static readonly Color[] ParticleColors =
    [
        Color.FromArgb(0xFF, 0xFF, 0x9E, 0xC7),   // 粉
        Color.FromArgb(0xFF, 0xB5, 0x8B, 0xFF),   // 紫
        Color.FromArgb(0xFF, 0x8C, 0xCB, 0xFF),   // 蓝
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)    // 白
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

        for (var i = 0; i < 10; i++)
            SpawnParticle(position);
    }

    /// <summary>点击处先炸开一团粉色雾</summary>
    private void SpawnRipple(Point position)
    {
        const double size = 160;

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
                    // rgba(255,158,199,.28) → rgba(181,139,255,.14) → 透明
                    new GradientStop { Color = Color.FromArgb(0x47, 0xFF, 0x9E, 0xC7), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0x24, 0xB5, 0x8B, 0xFF), Offset = 0.45 },
                    new GradientStop { Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 0.7 }
                }
            }
        };

        Canvas.SetLeft(ripple, position.X - size / 2);
        Canvas.SetTop(ripple, position.Y - size / 2);
        _layer.Children.Add(ripple);

        var storyboard = new Storyboard();

        var transform = (ScaleTransform)ripple.RenderTransform;

        storyboard.Children.Add(Build(transform, nameof(ScaleTransform.ScaleX), 0, 2, 0.45));
        storyboard.Children.Add(Build(transform, nameof(ScaleTransform.ScaleY), 0, 2, 0.45));
        storyboard.Children.Add(Build(ripple, "Opacity", 0.7, 0, 0.45));

        storyboard.Completed += (_, _) => _layer.Children.Remove(ripple);
        storyboard.Begin();
    }

    /// <summary>一颗往外飞的小圆点</summary>
    private void SpawnParticle(Point position)
    {
        var size = 6 + _random.NextDouble() * 14;
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
        var tx = (_random.NextDouble() - 0.5) * 80;
        var ty = (_random.NextDouble() - 0.5) * 80 - 25;

        var transform = (CompositeTransform)dot.RenderTransform;
        var storyboard = new Storyboard();

        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.TranslateX), 0, tx, 0.9));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.TranslateY), 0, ty, 0.9));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.ScaleX), 1, 0.25, 0.9));
        storyboard.Children.Add(Build(transform, nameof(CompositeTransform.ScaleY), 1, 0.25, 0.9));
        storyboard.Children.Add(Build(dot, "Opacity", 1.0, 0.0, 0.9));

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
