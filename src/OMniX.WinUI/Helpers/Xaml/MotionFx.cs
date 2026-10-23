using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：把 GlassFx（悬停上浮 + 按压回弹）铺到整个界面。
///
///     之前是给每个控件手写 fx:GlassFx.Attach="True"，结果只挂了首页两张卡和插件卡，
///     其它按钮、列表、卡片一个都没动 —— 这就是"鼠标移上去没反应"的原因。
///
///     改成挂一个 Scan 属性在页面根节点上，它会在布局变化时扫一遍可视化树，
///     给符合条件的控件补挂 GlassFx（已经挂过的会跳过），列表虚拟化新出来的项也能覆盖到。
/// </summary>
public static class MotionFx
{
    /// <summary>挂到页面根节点上开启动效扫描</summary>
    public static readonly DependencyProperty ScanProperty =
        DependencyProperty.RegisterAttached("Scan", typeof(bool), typeof(MotionFx),
            new PropertyMetadata(false, OnScanChanged));

    public static void SetScan(DependencyObject element, bool value) => element.SetValue(ScanProperty, value);

    public static bool GetScan(DependencyObject element) => (bool)element.GetValue(ScanProperty);

    // 扫描间隔：布局刚变完的那几十毫秒里会反复触发 LayoutUpdated，不用每次都扫
    private const long ThrottleMs = 700;

    private static long _lastScan;

    private static Panel? _overlay;
    private static ClickFx? _clickFx;

    private static SakuraFx? _sakura;
    private static Grid? _sakuraHost;

    private static FrameworkElement? _companionRoot;
    private static Panel? _companionOverlay;
    private static CompanionFx? _companion;
    private static bool _companionAttached;

    private static NavigationView? _nav;
    private static Panel? _navOverlay;
    private static NavIndicatorFx? _navIndicator;
    private static bool _navIndicatorAttached;

    private static bool _themeHooked;

    private static void OnScanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (e.NewValue is true)
            element.LayoutUpdated += OnLayoutUpdated;
        else
            element.LayoutUpdated -= OnLayoutUpdated;
    }

    private static void OnLayoutUpdated(object? sender, object e)
    {
        if (sender is not FrameworkElement root)
            return;

        var now = Environment.TickCount64;
        if (now - _lastScan < ThrottleMs)
            return;

        _lastScan = now;

        ScanTree(root);
    }

    /// <summary>扫一遍子树，给交互控件补上悬停 / 按压反馈</summary>
    public static void ScanTree(DependencyObject node)
    {
        // 简易版整体不动
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not DependencyObject child)
                continue;

            if (ShouldAttach(child) && !GlassFx.GetAttach(child))
                GlassFx.SetAttach(child, true);

            ScanTree(child);
        }
    }

    /// <summary>
    ///     哪些控件该有手感：
    ///     - 按钮类、开关类：点得到，就该有反馈
    ///     - 圆角较大的 Border：这是我们的卡片/磁贴，抬起来最明显
    ///     其它容器一律不挂，免得整个界面跟着抖。
    /// </summary>
    private static bool ShouldAttach(DependencyObject child) => child switch
    {
        Button or RepeatButton or ToggleButton or HyperlinkButton or DropDownButton or SplitButton => true,
        CheckBox or RadioButton or ComboBox or Slider or ToggleSwitch => true,
        NavigationViewItem or ListViewItem or GridViewItem or TreeViewItem => true,
        Border border => border.CornerRadius.TopLeft >= 12,
        _ => false
    };

    /// <summary>
    ///     点按反馈：一声"嗒" + 一团粒子。挂在页面根上用 AddHandler 收 PointerPressed
    ///     （handledEventsToo=true，所以按钮自己处理掉了事件这里也能收到），
    ///     往上找几层，命中交互控件就出音效和粒子。
    ///
    ///     顺手立刻扫一遍树，不等第一次 LayoutUpdated —— 已经挂过的元素会被跳过，
    ///     所以这里多扫一次没有副作用。
    /// </summary>
    public static void AttachClickFx(FrameworkElement root, Panel overlay)
    {
        _overlay = overlay;
        _clickFx = new ClickFx(overlay);

        root.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnPointerPressed), true);

        // 移动拖尾和悬停粒子，两个都做了节流，移动那个尤其要控住频率
        root.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(OnTrailMoved), true);

        root.AddHandler(UIElement.PointerEnteredEvent,
            new PointerEventHandler(OnHoverEntered), true);

        ScanTree(root);
    }

    // 拖尾 45ms 一次（参考网页是 25ms 配 2-3 颗，实测稳态粒子太多，放慢一档）；
    // 悬停 300ms 一次，鼠标划过一长串控件时不会每个都炸一下
    private const long TrailIntervalMs = 45;
    private const long HoverIntervalMs = 300;

    private static long _lastTrail;
    private static long _lastHover;

    private static void OnTrailMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_overlay is null || _clickFx is null)
            return;

        if (e.Pointer.PointerDeviceType is not Microsoft.UI.Input.PointerDeviceType.Mouse)
            return;

        var now = Environment.TickCount64;
        if (now - _lastTrail < TrailIntervalMs)
            return;

        _lastTrail = now;
        _clickFx.Trail(e.GetCurrentPoint(_overlay).Position);
    }

    private static void OnHoverEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_overlay is null || _clickFx is null)
            return;

        if (e.Pointer.PointerDeviceType is not Microsoft.UI.Input.PointerDeviceType.Mouse)
            return;

        // 只对真正的交互控件出粒子，划过文字和空白不应该有反应
        if (e.OriginalSource is not DependencyObject node || !IsInteractiveControl(node))
            return;

        var now = Environment.TickCount64;
        if (now - _lastHover < HoverIntervalMs)
            return;

        _lastHover = now;
        _clickFx.Hover(e.GetCurrentPoint(_overlay).Position);
    }

    /// <summary>
    ///     背景樱花花瓣。和悬停、点击那两套共用同一个开关：
    ///     标准版在宿主 Grid 最底层铺一层花瓣，切到简易版立刻整层移除。
    /// </summary>
    public static void AttachSakura(Grid host)
    {
        _sakuraHost = host;
        _sakura ??= new SakuraFx();

        HookThemeStyle();
        ApplySakura();
    }

    /// <summary>风格开关只订阅一次，樱花和光标共用同一个</summary>
    private static void HookThemeStyle()
    {
        if (_themeHooked || ThemeStyleService.Instance is null)
            return;

        ThemeStyleService.Instance.Changed += OnThemeStyleChanged;
        _themeHooked = true;
    }

    private static void ApplySakura()
    {
        if (_sakuraHost is null || _sakura is null)
            return;

        if (ThemeStyleService.Instance is { MotionEnabled: false })
            _sakura.Detach();
        else
            _sakura.Attach(_sakuraHost);
    }

    /// <summary>
    ///     跟着鼠标跑的看板娘。和樱花共用同一个风格开关，
    ///     切到简易版会立刻从界面上摘掉。
    /// </summary>
    public static void AttachCompanion(FrameworkElement root, Panel overlay)
    {
        _companionRoot = root;
        _companionOverlay = overlay;
        _companion ??= new CompanionFx(overlay);

        HookThemeStyle();
        ApplyCompanion();
    }

    private static void ApplyCompanion()
    {
        if (_companionRoot is null || _companion is null)
            return;

        if (ThemeStyleService.Instance is { MotionEnabled: false })
        {
            _companion.Detach();
            _companionAttached = false;
        }
        else if (!_companionAttached)
        {
            _companion.Attach(_companionRoot);
            _companionAttached = true;
        }
    }

    /// <summary>侧边栏选中项的滑动指示条，和樱花同一套开关</summary>
    public static void AttachNavIndicator(NavigationView nav, Panel overlay)
    {
        _nav = nav;
        _navOverlay = overlay;
        _navIndicator ??= new NavIndicatorFx(overlay);

        HookThemeStyle();
        ApplyNavIndicator();
    }

    private static void ApplyNavIndicator()
    {
        if (_nav is null || _navIndicator is null)
            return;

        if (ThemeStyleService.Instance is { MotionEnabled: false })
        {
            _navIndicator.Detach();
            _navIndicatorAttached = false;
        }
        else if (!_navIndicatorAttached)
        {
            _navIndicator.Attach(_nav);
            _navIndicatorAttached = true;
        }
    }

    private static void OnThemeStyleChanged(object? sender, OmniXThemeStyle style)
    {
        ApplySakura();
        ApplyCompanion();
        ApplyNavIndicator();
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is not (Microsoft.UI.Input.PointerDeviceType.Mouse
                or Microsoft.UI.Input.PointerDeviceType.Touch
                or Microsoft.UI.Input.PointerDeviceType.Pen))
            return;

        DependencyObject? node = e.OriginalSource as DependencyObject;

        // 明确是交互控件的，一路往上找到根都认；
        // 只有"圆角 Border 当卡片用"这种靠形状猜的才限制层数，
        // 否则点到页面空白处也会因为外层套了个圆角容器而乱响
        for (var depth = 0; node is not null; depth++, node = VisualTreeHelper.GetParent(node))
        {
            if (!IsInteractiveControl(node) && !(depth < CardSearchDepth && IsCardLike(node)))
                continue;

            UiSoundService.Instance?.PlayTap();

            if (_overlay is not null)
                _clickFx?.Burst(e.GetCurrentPoint(_overlay).Position);

            return;
        }
    }

    // 靠形状猜卡片时最多往上找几层
    private const int CardSearchDepth = 8;

    /// <summary>这些是货真价实的交互控件，命中就响</summary>
    private static bool IsInteractiveControl(DependencyObject node) => node switch
    {
        ButtonBase or CheckBox or RadioButton or ToggleButton or ToggleSwitch => true,
        ComboBox or Slider or AutoSuggestBox or Expander => true,
        NavigationViewItem or SelectorItem or TreeViewItem or TabViewItem => true,
        _ => false
    };

    /// <summary>圆角够大的 Border 按卡片算，属于猜的，所以限制层数</summary>
    private static bool IsCardLike(DependencyObject node) =>
        node is Border { CornerRadius.TopLeft: >= 12 };
}
