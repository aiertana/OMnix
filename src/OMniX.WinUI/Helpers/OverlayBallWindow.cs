using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     圆形悬浮球：Win32 分层窗口（UpdateLayeredWindow）直接画带透明通道的 PNG。
///     WinUI 窗口做不到真正的透明背景（根元素透明也会露出一块方底），
///     所以悬浮球单独用裸 Win32 窗口：只有圆形那部分像素可见，四周全透明。
///     窗口带 WS_EX_NOACTIVATE，点击不会把游戏切到后台。
/// </summary>
internal sealed class OverlayBallWindow : IDisposable
{
    private const int Size = 48; // 与 Assets/AppIconCircle.png 尺寸一致

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_NCCREATE = 0x0081;
    private const int WM_DESTROY = 0x0002;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_HOTKEY = 0x0312;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOWNA = 8;
    private const int SW_HIDE = 0;

    private static readonly IntPtr HwndTopMost = new(-1);

    private static OverlayBallWindow? _current;

    /// <summary>
    ///     窗口过程委托。静态字段持有，防止被 GC 回收后回调崩溃。
    /// </summary>
    private static readonly WndProcDelegate WndProcStatic = WndProc;

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private bool _dragging;
    private bool _pressed;
    private OverlayNative.POINT _dragStartCursor;
    private OverlayNative.RECT _dragStartWindow;

    /// <summary>
    ///     悬浮球被单击（非拖拽）。
    /// </summary>
    public event Action? Clicked;

    /// <summary>
    ///     全局热键（Alt+C）触发。
    /// </summary>
    public event Action? HotkeyPressed;

    public IntPtr Hwnd { get; private set; }

    public bool Visible { get; private set; }

    /// <summary>
    ///     创建并显示悬浮球。x/y 传 -1 表示自动放到主屏右上角。
    /// </summary>
    public async Task InitializeAsync(double x, double y)
    {
        if (Hwnd != IntPtr.Zero)
        {
            Show();
            return;
        }

        _current = this;

        RegisterClassOnce();
        CreateWindow(x, y);
        await PaintBallAsync();
        Show();
    }

    public void Show()
    {
        if (Hwnd != IntPtr.Zero)
        {
            ShowWindow(Hwnd, SW_SHOWNA);
            Visible = true;
        }
    }

    public void Hide()
    {
        if (Hwnd != IntPtr.Zero)
        {
            ShowWindow(Hwnd, SW_HIDE);
            Visible = false;
        }
    }

    /// <summary>
    ///     拖拽结束后由持有方把当前位置存回设置。
    /// </summary>
    public (double X, double Y) GetPosition()
    {
        GetWindowRect(Hwnd, out var rect);
        return (rect.Left, rect.Top);
    }

    #region 窗口类与窗口过程

    private const string ClassName = "OMniX_OverlayBall";
    private static bool _classRegistered;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    private static void RegisterClassOnce()
    {
        if (_classRegistered)
        {
            return;
        }

        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcStatic),
            hInstance = Marshal.GetHINSTANCE(typeof(OverlayBallWindow).Module),
            lpszClassName = ClassName
        };

        RegisterClassW(ref wc);
        _classRegistered = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var ball = _current;
        if (ball is null)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case WM_NCCREATE:
                ball.Hwnd = hWnd;
                return DefWindowProc(hWnd, msg, wParam, lParam);

            case WM_HOTKEY:
                ball.HotkeyPressed?.Invoke();
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
                GetCursorPos(out ball._dragStartCursor);
                GetWindowRect(hWnd, out ball._dragStartWindow);
                ball._dragging = false;
                ball._pressed = true;
                SetCapture(hWnd);
                return IntPtr.Zero;

            case WM_MOUSEMOVE:
                // 没按住就别动窗口 —— 否则鼠标一靠近悬浮球，球就跟着鼠标跑
                if (!ball._pressed)
                {
                    return IntPtr.Zero;
                }

                GetCursorPos(out var move);

                if (!ball._dragging &&
                    (Math.Abs(move.X - ball._dragStartCursor.X) >= 3 ||
                     Math.Abs(move.Y - ball._dragStartCursor.Y) >= 3))
                {
                    ball._dragging = true;
                }

                if (ball._dragging)
                {
                    var left = ball._dragStartWindow.Left + move.X - ball._dragStartCursor.X;
                    var top = ball._dragStartWindow.Top + move.Y - ball._dragStartCursor.Y;
                    SetWindowPos(hWnd, HwndTopMost, left, top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                    ball.Dragging?.Invoke(left, top);
                }

                return IntPtr.Zero;

            case WM_LBUTTONUP:
                ReleaseCapture();
                ball._pressed = false;
                GetCursorPos(out var up);

                // 位移很小才算点击，拖过就算移动位置
                if (!ball._dragging &&
                    Math.Abs(up.X - ball._dragStartCursor.X) < 3 &&
                    Math.Abs(up.Y - ball._dragStartCursor.Y) < 3)
                {
                    ball.Clicked?.Invoke();
                }
                else
                {
                    ball.DragFinished?.Invoke();
                }

                ball._dragging = false;
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    ///     拖拽中（每移动一次就发一次，带窗口左上角屏幕坐标）。
    ///     Mod 面板开着时靠它让面板跟着球一起走。
    /// </summary>
    public event Action<int, int>? Dragging;

    /// <summary>
    ///     拖拽结束（位置变了）。
    /// </summary>
    public event Action? DragFinished;

    #endregion

    #region 创建窗口与绘制

    private void CreateWindow(double x, double y)
    {
        var (px, py) = ResolvePosition(x, y);

        Hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            ClassName, "OMniX", WS_POPUP,
            px, py, Size, Size,
            IntPtr.Zero, IntPtr.Zero, Marshal.GetHINSTANCE(typeof(OverlayBallWindow).Module), IntPtr.Zero);
    }

    private static (int X, int Y) ResolvePosition(double x, double y)
    {
        if (x > 0 && y > 0)
        {
            return ((int)x, (int)y);
        }

        // 第一次用：主屏右上角。GetDesktopWindow 的 rect 不可靠，用 GetSystemMetrics
        var screenWidth = GetSystemMetrics(0); // SM_CXSCREEN
        return (screenWidth - Size - 40, 90);
    }

    /// <summary>
    ///     读 AppIconCircle.png（Bgra8 + Premultiplied），塞进 DIB 后交给 UpdateLayeredWindow。
    /// </summary>
    private async Task PaintBallAsync()
    {
        // 未打包应用里 ms-appx:/// 不一定能用，直接读 exe 旁边的 Assets 文件
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIconCircle.png");
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var bytes = pixelData.DetachPixelData();

        var nonZero = 0;
        foreach (var b in bytes)
        {
            if (b != 0)
            {
                nonZero++;
            }
        }

        if (nonZero == 0)
        {
            Serilog.Log.Warning("悬浮球图标解码出来是空的，球会显示为全透明");
        }

        var hdcScreen = GetDC(IntPtr.Zero);
        var hdcMem = CreateCompatibleDC(hdcScreen);

        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = Size,
            biHeight = -Size, // 负数 = 自上而下
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        var hBitmap = CreateDIBSection(hdcMem, ref bi, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(bytes, 0, bits, Math.Min(bytes.Length, Size * Size * 4));
        var old = SelectObject(hdcMem, hBitmap);

        // pptDst 传 NULL：窗口位置由 CreateWindowEx / SetWindowPos 决定，这里只管画内容
        var size = new SIZE { cx = Size, cy = Size };
        var src = new OverlayNative.POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0, // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1 // AC_SRC_ALPHA
        };

        var ulwOk = UpdateLayeredWindow(Hwnd, hdcScreen, IntPtr.Zero, ref size, hdcMem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);

        if (!ulwOk)
        {
            Serilog.Log.Warning("悬浮球 UpdateLayeredWindow 失败，错误码={Err}", Marshal.GetLastWin32Error());
        }

        SelectObject(hdcMem, old);
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    /// <summary>
    ///     UpdateLayeredWindow 的 psize 参数要的是 SIZE（两个 int），不是 POINTS（两个 short）。
    ///     用错结构大小会直接返回 E_INVALIDARG。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    #endregion

    public void Dispose()
    {
        if (Hwnd != IntPtr.Zero)
        {
            DestroyWindow(Hwnd);
            Hwnd = IntPtr.Zero;
        }
    }

    #region P/Invoke

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out OverlayNative.RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out OverlayNative.POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint iUsage,
        out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref OverlayNative.POINT pptSrc, uint crKey,
        ref BLENDFUNCTION pblend, uint dwFlags);

    #endregion
}
