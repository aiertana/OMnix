using System.Runtime.InteropServices;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     全局热键用的 Win32 封装。WinUI 3 没有 HwndSource.AddHook，
///     所以用 RegisterHotKey + SetWindowSubclass 自己接 WM_HOTKEY。
/// </summary>
internal static class OverlayNative
{
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>
    ///     C 键的虚拟键码。
    /// </summary>
    public const uint VK_C = 0x43;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    ///     取屏幕坐标下的鼠标位置。拖拽要按屏幕坐标算位移 ——
    ///     用「相对窗口」的坐标的话，窗口一动，鼠标相对位置跟着变，位移会被自己吃掉。
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("comctl32.dll")]
    internal static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIDSubclass,
        UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIDSubclass);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    ///     SetWindowSubclass 的回调。委托实例必须一直持有，否则被 GC 回收后回调会崩。
    /// </summary>
    internal delegate int SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass,
        uint dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    ///     置顶（HWND_TOPMOST）。
    /// </summary>
    public static readonly IntPtr HwndTopMost = new(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
}
