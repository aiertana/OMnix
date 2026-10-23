using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OMniX.WinUI.Services;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;
using Windows.UI;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：背景樱花花瓣飘落。
///
///     整片花瓣是矢量 Path，动画全部交给 Windows.UI.Composition：
///     每片花瓣三个动画（下落加左右摆动的 Translation、Z 轴旋转、透明度渐变），
///     都用关键帧跑在合成线程上，不用 Storyboard，也不用定时器逐帧更新。
///
///     花瓣装在宿主 Grid 最底层的 Canvas 里，ZIndex = 0 且不接收命中测试，
///     所以不会挡住任何点击。简易版（静态玻璃）下整层移除，一个 Visual 都不留。
/// </summary>
public sealed class SakuraFx
{
    // 想调密度和节奏，改这几个常量就够了
    private const int PetalCount = 28;

    private const double MinSize = 8;
    private const double MaxSize = 16;

    private const double MinFallSeconds = 4;
    private const double MaxFallSeconds = 8;

    private const double MinSwingAmplitude = 20;
    private const double MaxSwingAmplitude = 50;

    private const double MinSwingPeriod = 2;
    private const double MaxSwingPeriod = 4;

    private const float StartOpacity = 0.8f;
    private const float EndOpacity = 0.3f;

    // 花瓣从屏幕上边界之外一点点出现，落到下边界之外一点点再重置
    private const double EdgeMargin = 20;

    // 摆动曲线采样点数：点越多越圆滑，48 个点对 4 到 8 秒的下落足够平滑
    private const int SwingSamples = 48;

    // 窗口拖动时会连续触发 SizeChanged，重建一次太贵，攒一波再建
    private const int RebuildDelayMs = 220;

    /// <summary>三种花瓣色：半透明粉、更淡的粉、淡紫粉</summary>
    private static readonly Color[] PetalColors =
    [
        Color.FromArgb(0x33, 0xFF, 0xB3, 0xD9),
        Color.FromArgb(0x40, 0xFF, 0xD9, 0xEC),
        Color.FromArgb(0x26, 0xC9, 0xA8, 0xFF)
    ];

    private readonly Random _random = new();

    private Grid? _host;
    private Canvas? _layer;
    private ContainerVisual? _rootVisual;
    private bool _running;
    private int _rebuildToken;

    // 所有花瓣共用同一份几何（Path.Data 可以共享），省掉 28 次路径构建


    /// <summary>
    ///     挂到宿主 Grid 上。简易版直接调 Detach，什么都不建。
    /// </summary>
    public void Attach(Grid host)
    {
        if (ThemeStyleService.Instance is { MotionEnabled: false })
        {
            Detach();
            return;
        }

        if (_running && ReferenceEquals(_host, host))
            return;

        Detach();

        _host = host;
        EnsureLayer();

        // 必须先置位：层的首次布局是异步的，SizeChanged 会在下面这次 BuildPetals
        // 之前就到，那时尺寸还是 0，得靠它延迟重建一次才画得出来
        _running = true;

        BuildPetals();
    }

    /// <summary>
    ///     停掉全部动画、清掉花瓣、把整层从界面上摘掉。
    ///     花瓣 Visual 归 XAML 管，从树上摘下来之后会随元素一起回收，不手动 Dispose。
    /// </summary>
    public void Detach()
    {
        if (_host is not null && _layer is not null)
            _host.Children.Remove(_layer);

        if (_layer is not null)
        {
            _layer.SizeChanged -= OnLayerSizeChanged;
            StopPetals();
            _layer.Children.Clear();
            _layer = null;
        }

        _rootVisual = null;
        _host = null;
        _running = false;
        _rebuildToken++;
    }

    private void EnsureLayer()
    {
        if (_host is null || _layer is not null)
            return;

        var layer = new Canvas
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 铺满整个宿主（跨所有行列），压在最底层
        Grid.SetRowSpan(layer, Math.Max(_host.RowDefinitions.Count, 1));
        Grid.SetColumnSpan(layer, Math.Max(_host.ColumnDefinitions.Count, 1));
        Canvas.SetZIndex(layer, 0);

        // 插在最前面，等于画在所有内容底下
        _host.Children.Insert(0, layer);

        _layer = layer;
        _layer.SizeChanged += OnLayerSizeChanged;

        // 这一层的 visual 天然就是装所有花瓣的容器
        _rootVisual = ElementCompositionPreview.GetElementVisual(_layer) as ContainerVisual;
    }

    private void BuildPetals()
    {
        if (_layer is null)
            return;

        StopPetals();
        _layer.Children.Clear();

        var width = _layer.ActualWidth;
        var height = _layer.ActualHeight;

        // 还没布局完就先不画，等 SizeChanged 再来
        if (width <= 0 || height <= 0)
            return;

        for (var i = 0; i < PetalCount; i++)
            AddPetal(width, height);
    }

    private void AddPetal(double width, double height)
    {
        if (_layer is null)
            return;

        var size = MinSize + _random.NextDouble() * (MaxSize - MinSize);
        var color = PetalColors[_random.Next(PetalColors.Length)];

        var petal = new ShapePath
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Fill,
            Data = GetPetalGeometry(),
            Fill = new SolidColorBrush(color),
            IsHitTestVisible = false
        };

        _layer.Children.Add(petal);

        var visual = ElementCompositionPreview.GetElementVisual(petal);
        if (visual is null)
            return;

        // 让 Composition 能改它的位置，且不影响 XAML 自己的布局
        ElementCompositionPreview.SetIsTranslationEnabled(petal, true);

        var fallSeconds = MinFallSeconds + _random.NextDouble() * (MaxFallSeconds - MinFallSeconds);
        var duration = TimeSpan.FromSeconds(fallSeconds);

        // 错开出场：不然 28 片会整齐地一起往下掉
        var delay = TimeSpan.FromSeconds(_random.NextDouble() * fallSeconds);
        var compositor = visual.Compositor;

        // 一条曲线同时表达下落和左右摆动：
        // Y 从屏幕上方线性落到底部，X 按正弦来回摆，
        // 下落时间里刚好走完整数个摆动周期，循环回到顶部时 X 也回到原位，接缝看不出来
        var baseX = (float)(_random.NextDouble() * width);
        var amplitude = (float)(MinSwingAmplitude + _random.NextDouble() * (MaxSwingAmplitude - MinSwingAmplitude));
        var swingPeriod = MinSwingPeriod + _random.NextDouble() * (MaxSwingPeriod - MinSwingPeriod);
        var cycles = Math.Max(fallSeconds / swingPeriod, 1);

        var drift = compositor.CreateVector3KeyFrameAnimation();
        drift.Duration = duration;
        drift.DelayTime = delay;
        drift.IterationBehavior = AnimationIterationBehavior.Forever;

        for (var i = 0; i <= SwingSamples; i++)
        {
            var progress = (double)i / SwingSamples;

            var y = -EdgeMargin + (height + EdgeMargin * 2) * progress;
            var x = baseX + Math.Sin(progress * cycles * 2 * Math.PI) * amplitude;

            drift.InsertKeyFrame((float)progress, new Vector3((float)x, (float)y, 0));
        }

        visual.StartAnimation("Translation", drift);

        // 自转：方向随机，整圈数保证循环时角度接得上
        var spins = 1 + _random.Next(2);
        var direction = _random.Next(2) == 0 ? -1 : 1;

        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.Duration = duration;
        spin.DelayTime = delay;
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, direction * 360f * spins);

        visual.StartAnimation("RotationAngleInDegrees", spin);

        // 越落越淡，到底消失，下一轮从顶部重新开始
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.DelayTime = delay;
        fade.IterationBehavior = AnimationIterationBehavior.Forever;
        fade.InsertKeyFrame(0f, StartOpacity);
        fade.InsertKeyFrame(1f, EndOpacity);

        visual.StartAnimation("Opacity", fade);
    }

    private void StopPetals()
    {
        if (_layer is null)
            return;

        foreach (var child in _layer.Children)
        {
            if (child is not ShapePath petal)
                continue;

            var visual = ElementCompositionPreview.GetElementVisual(petal);
            if (visual is null)
                continue;

            visual.StopAnimation("Translation");
            visual.StopAnimation("RotationAngleInDegrees");
            visual.StopAnimation("Opacity");
        }
    }

    private void OnLayerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 飘落范围是按层尺寸算的，窗口变了就得重算
        var token = Interlocked.Increment(ref _rebuildToken);
        var queue = _layer?.DispatcherQueue;

        if (queue is null)
            return;

        // 拖窗口时会连续触发，攒够一波再重建，最后一次会落到正确尺寸上
        _ = Task.Delay(RebuildDelayMs).ContinueWith(_ =>
            queue.TryEnqueue(() =>
            {
                if (token != _rebuildToken || !_running)
                    return;

                BuildPetals();
            }));
    }

    /// <summary>
    ///     樱花花瓣：底部收成一个尖，顶缘中间有个小凹口。
    ///     画在 16 x 16 的坐标系里，靠 Stretch.Fill 缩到实际尺寸。
    /// </summary>
    private static PathGeometry GetPetalGeometry()
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(8, 0.8),
            IsClosed = true
        };

        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(6.6, 2.2),
            Point2 = new Point(4.6, 0.9),
            Point3 = new Point(3.6, 2.6)
        });

        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(1.2, 5.6),
            Point2 = new Point(0.6, 10.2),
            Point3 = new Point(8, 15.6)
        });

        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(15.4, 10.2),
            Point2 = new Point(14.8, 5.6),
            Point3 = new Point(12.4, 2.6)
        });

        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(11.4, 0.9),
            Point2 = new Point(9.4, 2.2),
            Point3 = new Point(8, 0.8)
        });

        // 一份几何只能挂在一个 Path 上，第二片再用同一个实例会抛 ArgumentException，
        // 所以每片各自建一份（一共 28 次，只在重建时发生，开销可以忽略）
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return geometry;
    }
}
