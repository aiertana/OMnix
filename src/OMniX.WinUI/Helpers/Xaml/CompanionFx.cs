using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     跟着鼠标跑的看板娘。
///
///     原本想做的是"把看板娘做成鼠标光标"，但 WinUI 3 不允许：
///     把窗口类光标换成看板娘之后（实测 GCLP_HCURSOR 确实改成功了，
///     句柄从系统箭头 65539 变成自定义句柄），系统实际显示的仍然是箭头 ——
///     WinUI 在输入时会显式 SetCursor 覆盖掉，
///     子类化 WM_SETCURSOR、鼠标移动后补一刀、改类光标，三条路都被它压住。
///     而且系统箭头藏不住，真做替换就会变成"箭头 + 看板娘"两个光标。
///
///     所以改成现在这样：看板娘是一个自己画的图层，跟在鼠标右下角一点，
///     不挡箭头热点，按下左键换成张嘴那张。完全不碰系统光标。
/// </summary>
public sealed class CompanionFx
{
    // 贴着鼠标：尺寸按光标大小来，偏移只留一点点，箭头热点不被挡
    private const double OffsetX = 3;
    private const double OffsetY = 4;
    private const double Size = 48;

    private readonly Panel _host;
    private readonly Image _image;
    private readonly BitmapImage _idle;
    private readonly BitmapImage _pressed;

    private bool _visible;

    /// <summary>已经挂过事件的根节点，重复 Attach 时不要重复订阅</summary>
    private FrameworkElement? _root;

    public CompanionFx(Panel host)
    {
        _host = host;

        _idle = Load("cursor-default.png");
        _pressed = Load("cursor-pressed.png");

        _image = new Image
        {
            Width = Size,
            Height = Size,
            Source = _idle,
            IsHitTestVisible = false
        };

        Canvas.SetZIndex(_image, 100);

        // 先停到窗口外。用 Opacity=0 藏着的话，Image 是"不可见"状态，
        // BitmapImage 会懒解码，第一次跟随的那一下是空白的；摆到界面外它照样解码
        Park();

        _host.Children.Add(_image);
    }

    private static BitmapImage Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cursors", fileName);

        // 直接拼 "file:///" + 盘符路径会被当成非法 Uri，图片根本加载不出来。
        // 用 base Uri 组合才是可用的写法
        var uri = new Uri(new Uri("file:///"), path);

        return new BitmapImage(uri);
    }

    public void Attach(FrameworkElement root)
    {
        // Detach() 会把这个 Image 从 Canvas 上摘掉（简易版要彻底不出现），
        // 切回标准版时必须放回去，否则事件照样在跑、坐标也照样在算，
        // 但元素根本不在视觉树里 —— 表现就是"切回来之后鼠标宠物没了"
        if (!_host.Children.Contains(_image))
            _host.Children.Add(_image);

        if (!ReferenceEquals(_root, root))
        {
            if (_root is not null)
            {
                _root.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved));
                _root.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed));
                _root.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
                _root.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointerExited));
            }

            root.AddHandler(UIElement.PointerMovedEvent,
                new PointerEventHandler(OnPointerMoved), true);

            root.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnPointerPressed), true);

            root.AddHandler(UIElement.PointerReleasedEvent,
                new PointerEventHandler(OnPointerReleased), true);

            root.AddHandler(UIElement.PointerExitedEvent,
                new PointerEventHandler(OnPointerExited), true);

            _root = root;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsMouse(e.Pointer))
            return;

        var point = e.GetCurrentPoint(_host).Position;
        Move(point.X, point.Y);

        if (!_visible)
            Show(true);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsMouse(e.Pointer))
            _image.Source = _pressed;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsMouse(e.Pointer))
            _image.Source = _idle;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!IsMouse(e.Pointer))
            return;

        // PointerExited 会在每个子元素上冒泡触发，鼠标从 A 划到 B 途经一堆控件时
        // 会不停收到。拿事件里的坐标判断也不可靠，直接问系统光标实际在哪，
        // 真的出了窗口才藏起来
        if (!GetCursorPos(out var cursor))
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;

        var inside = cursor.X >= rect.Left && cursor.X < rect.Right
                     && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;

        if (!inside)
            Show(false);
    }

    private static bool IsMouse(Pointer pointer) =>
        pointer.PointerDeviceType is Microsoft.UI.Input.PointerDeviceType.Mouse;

    /// <summary>
    ///     直接用 Canvas 定位。之前试过 Composition 的 Translation（先
    ///     SetIsTranslationEnabled 再往属性集里塞 "Translation"），元素是加进去了、
    ///     事件也进来了，但屏幕上什么都没有 —— 这条路在 Canvas 子元素上不成立，别绕了
    /// </summary>
    private void Move(double x, double y)
    {
        Canvas.SetLeft(_image, x + OffsetX);
        Canvas.SetTop(_image, y + OffsetY);
    }

    private void Show(bool show)
    {
        _visible = show;

        if (!show)
            Park();
    }

    /// <summary>移到窗口外，等于藏起来，但不会让图片退回未解码状态</summary>
    private void Park()
    {
        Canvas.SetLeft(_image, -Size - 40);
        Canvas.SetTop(_image, -Size - 40);
    }

    /// <summary>从界面上摘掉，简易版下彻底不出现</summary>
    public void Detach()
    {
        Show(false);

        if (_host.Children.Contains(_image))
            _host.Children.Remove(_image);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);
}
