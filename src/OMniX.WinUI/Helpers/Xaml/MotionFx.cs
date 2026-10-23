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
    private static bool _sakuraHooked;

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

        var attached = 0;
        ScanCore(node, ref attached);

#if DEBUG
        Trace($"scan: attached {attached} new elements");
#endif
    }

    private static void ScanCore(DependencyObject node, ref int attached)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not DependencyObject child)
                continue;

            if (ShouldAttach(child) && !GlassFx.GetAttach(child))
            {
                GlassFx.SetAttach(child, true);
                attached++;
            }

            ScanCore(child, ref attached);
        }
    }

#if DEBUG
    private static void Trace(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omnix_motion.log");
            System.IO.File.AppendAllText(path,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 诊断用，失败就失败
        }
    }
#endif

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

        ScanTree(root);
    }

    /// <summary>
    ///     背景樱花花瓣。和悬停、点击那两套共用同一个开关：
    ///     标准版在宿主 Grid 最底层铺一层花瓣，切到简易版立刻整层移除。
    /// </summary>
    public static void AttachSakura(Grid host)
    {
        _sakuraHost = host;
        _sakura ??= new SakuraFx();

        if (!_sakuraHooked && ThemeStyleService.Instance is not null)
        {
            ThemeStyleService.Instance.Changed += OnThemeStyleChanged;
            _sakuraHooked = true;
        }

        ApplySakura();
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

    private static void OnThemeStyleChanged(object? sender, OmniXThemeStyle style) => ApplySakura();

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is not (Microsoft.UI.Input.PointerDeviceType.Mouse
                or Microsoft.UI.Input.PointerDeviceType.Touch
                or Microsoft.UI.Input.PointerDeviceType.Pen))
            return;

        DependencyObject? node = e.OriginalSource as DependencyObject;

        // 只往上找 6 层：交互控件一般在源的近处，也不用每次循环到根
        for (var depth = 0; depth < 6 && node is not null; depth++)
        {
            if (ShouldPlay(node))
            {
                UiSoundService.Instance?.PlayTap();

                if (_overlay is not null)
                    _clickFx?.Burst(e.GetCurrentPoint(_overlay).Position);

                return;
            }

            node = VisualTreeHelper.GetParent(node);
        }
    }

    private static bool ShouldPlay(DependencyObject node) => node switch
    {
        ButtonBase or CheckBox or RadioButton or ToggleButton or ToggleSwitch => true,
        ComboBox or Slider or AutoSuggestBox => true,
        NavigationViewItem or ListViewItem or GridViewItem or TreeViewItem => true,
        Border border => border.CornerRadius.TopLeft >= 12,
        _ => false
    };
}
