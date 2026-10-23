using System.Numerics;
using Windows.Foundation;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     侧边栏选中项的滑动指示条。
///     选中哪一项，这条就带弹性地滑到哪一项旁边，400ms、cubic-bezier(.34,1.56,.64,1)。
///
///     指示条是画在宿主 Canvas 上的（不塞进 NavigationView 的模板里），
///     位置靠选中项 TransformToVisual 算出来，所以面板展开折叠、项目增删都能跟着走。
/// </summary>
public sealed class NavIndicatorFx
{
    private const double DurationMs = 400;
    private const double Thickness = 3;
    private const double HeightRatio = 0.55;
    private const double LeftMargin = 6;

    private readonly Panel _host;
    private readonly Border _indicator;
    private NavigationView? _nav;

    private Visual? _visual;
    private bool _enabled;

    public NavIndicatorFx(Panel host)
    {
        _host = host;

        _indicator = new Border
        {
            Width = Thickness,
            Height = 0,
            CornerRadius = new CornerRadius(Thickness / 2),
            Background = ResolveAccentBrush(),
            IsHitTestVisible = false,
            Opacity = 0
        };

        Canvas.SetZIndex(_indicator, 98);
        _host.Children.Add(_indicator);

        _visual = ElementCompositionPreview.GetElementVisual(_indicator);
        ElementCompositionPreview.SetIsTranslationEnabled(_indicator, true);
    }

    /// <summary>
    ///     主题资源是按当前主题解析的，直接索引不一定拿得到，
    ///     取不到就退回主色对应的纯色画刷，免得整条指示器变成空的
    /// </summary>
    private static Brush ResolveAccentBrush()
    {
        var resources = Application.Current.Resources;

        if (resources.TryGetValue("OmniXAccentBrush", out var value) && value is Brush brush)
            return brush;

        return new SolidColorBrush(Microsoft.UI.Colors.HotPink);
    }

    public void Attach(NavigationView nav)
    {
        _nav = nav;
        _enabled = true;

        nav.SelectionChanged += OnSelectionChanged;
        nav.DisplayModeChanged += OnLayoutChanged;
        nav.SizeChanged += OnSizeChanged;

        // 菜单项是运行时插进来的，布局稳定之后再摆一次
        nav.Loaded += (_, _) => Update(true);
        Update(true);
    }

    public void Detach()
    {
        _enabled = false;

        if (_nav is not null)
        {
            _nav.SelectionChanged -= OnSelectionChanged;
            _nav.DisplayModeChanged -= OnLayoutChanged;
            _nav.SizeChanged -= OnSizeChanged;
        }

        _indicator.Opacity = 0;
    }

    private void OnSelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args) => Update(false);

    private void OnLayoutChanged(NavigationView sender, object args) => Update(true);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update(true);

    private void Update(bool immediate)
    {
        if (!_enabled || _nav?.SelectedItem is not FrameworkElement item || _visual is null)
        {
            _indicator.Opacity = 0;
            return;
        }

        // 插件那两项是开关不是页面，选中它们时不显示指示条
        if (item is NavigationViewItem { SelectsOnInvoked: false })
        {
            _indicator.Opacity = 0;
            return;
        }

        var height = item.ActualHeight;
        if (height <= 0)
            return;

        var position = item.TransformToVisual(_host).TransformPoint(new Point(0, 0));

        var x = position.X + LeftMargin;
        var y = position.Y + height * (1 - HeightRatio) / 2;

        _indicator.Height = height * HeightRatio;
        _indicator.Opacity = 1;

        if (immediate)
        {
            _visual.StopAnimation("Translation");
            _visual.Properties.InsertVector3("Translation", new Vector3((float)x, (float)y, 0));
            return;
        }

        var status = _visual.Properties.TryGetVector3("Translation", out var from);
        var current = status == CompositionGetValueStatus.Succeeded ? from : Vector3.Zero;

        var compositor = _visual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(DurationMs);

        // 文档给的弹性曲线，末端会稍微冲过头再回来。
        // Composition 的缓动是挂在关键帧上的，动画对象本身没有 EasingFunction 属性
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

        animation.InsertKeyFrame(0f, current);
        animation.InsertKeyFrame(1f, new Vector3((float)x, (float)y, 0), easing);

        _visual.StartAnimation("Translation", animation);
    }
}
