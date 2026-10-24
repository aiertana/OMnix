using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using OMniX.WinUI.Services;
using Windows.Foundation;
using Windows.UI;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：鼠标移动拖出来的粒子尾巴。
///
///     用 Composition 的 SpriteVisual 画，不走 XAML 元素：
///     移动是持续触发的，每 25ms 就是 2-3 颗，用 XAML 元素就要不停
///     new Ellipse、加进树、起 Storyboard、再摘掉，稳态开销比粒子本身还大。
///     SpriteVisual 只是合成层里的一个小矩形，创建和销毁都便宜得多。
///
///     所有粒子挂在一个 ContainerVisual 下面（一个元素只能 SetElementChildVisual
///     一个 visual，所以得有个容器当中转），动画结束就把自己摘掉并 Dispose。
///     简易版直接不出粒子。
/// </summary>
public sealed class MouseTrailFx
{
    // 执行指令：25ms 一批，每批 2-3 颗
    private const long IntervalMs = 25;

    private const int MinCount = 2;
    private const int MaxCount = 4; // Next 的上界是排他的，写 4 才是 2~3

    // 二开：粒子原来做到 4~12，跟着鼠标走显得哗啦啦的一大把，压到一半。
    // 飘散的范围也跟着收了：小粒子飞 40 多像素看着像炸开，不聚拢
    private const double MinSize = 2;
    private const double MaxSize = 6;

    // 粒子寿命 800ms，往上飘的同时往随机方向散
    private const double LifeSeconds = 0.8;

    private const double StartOpacity = 0.75f;

    private const double SpreadX = 18;
    private const double RiseMin = 12;
    private const double RiseMax = 30;

    // 粒子色：主色 / 淡粉 / 浅蓝 / 白
    private static readonly Color[] ParticleColors =
    [
        Color.FromArgb(0xFF, 0xE8, 0x4C, 0x6D),
        Color.FromArgb(0xFF, 0xFF, 0xB3, 0xD1),
        Color.FromArgb(0xFF, 0x7E, 0xC8, 0xFF),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
    ];

    private readonly Panel _layer;
    private readonly Random _random = new();

    private ContainerVisual? _root;
    private long _lastSpawn;

    public MouseTrailFx(Panel layer)
    {
        _layer = layer;

        // 元素还没进视觉树时拿到的 visual 挂不上去，等 Loaded 之后再建容器
        if (layer.IsLoaded)
            EnsureRoot();
        else
            layer.Loaded += (_, _) => EnsureRoot();
    }

    public void Attach(FrameworkElement root)
    {
        root.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(OnPointerMoved), true);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // 触摸和笔不需要拖尾
        if (e.Pointer.PointerDeviceType is not Microsoft.UI.Input.PointerDeviceType.Mouse)
            return;

        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        var now = Environment.TickCount64;
        if (now - _lastSpawn < IntervalMs)
            return;

        _lastSpawn = now;

        var position = e.GetCurrentPoint(_layer).Position;
        var count = MinCount + _random.Next(MaxCount - MinCount);

        for (var i = 0; i < count; i++)
            Spawn(position);
    }

    private void Spawn(Point position)
    {
        if (_root is null)
            return;

        var compositor = _root.Compositor;
        var size = (float)(MinSize + _random.NextDouble() * (MaxSize - MinSize));
        var color = ParticleColors[_random.Next(ParticleColors.Length)];

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new Vector2(size, size);
        sprite.Brush = compositor.CreateColorBrush(color);
        sprite.Opacity = (float)StartOpacity;

        // 粒子中心对准鼠标，别偏到一边去
        sprite.Offset = new Vector3((float)position.X - size / 2, (float)position.Y - size / 2, 0);

        _root.Children.InsertAtTop(sprite);

        var life = TimeSpan.FromSeconds(LifeSeconds);

        // 往上飘，同时左右随机散一点
        var dx = (float)((_random.NextDouble() - 0.5) * SpreadX);
        var dy = (float)-(RiseMin + _random.NextDouble() * (RiseMax - RiseMin));

        var move = compositor.CreateVector3KeyFrameAnimation();
        move.Duration = life;
        move.InsertKeyFrame(0f, sprite.Offset);
        move.InsertKeyFrame(1f, sprite.Offset + new Vector3(dx, dy, 0));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = life;
        fade.InsertKeyFrame(0f, (float)StartOpacity);
        fade.InsertKeyFrame(1f, 0f);

        // 合成动画没有 Completed 事件，用 ScopedBatch 收"这批动画跑完了"的通知，
        // 收到就把粒子摘掉，不然淡成透明的粒子会一直堆在容器里
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        batch.Completed += (_, _) =>
        {
            _root.Children.Remove(sprite);
            sprite.Dispose();
        };

        sprite.StartAnimation("Offset", move);
        sprite.StartAnimation("Opacity", fade);

        batch.End();
    }

    private void EnsureRoot()
    {
        if (_root is not null)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(_layer);
        if (visual?.Compositor is null)
            return;

        _root = visual.Compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_layer, _root);
    }

    /// <summary>简易版下把已经画出来的粒子全清掉</summary>
    public void Clear()
    {
        if (_root is null)
            return;

        foreach (var child in _root.Children)
            child.Dispose();

        _root.Children.RemoveAll();
    }
}
