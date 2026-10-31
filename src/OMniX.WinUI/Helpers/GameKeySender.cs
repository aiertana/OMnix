using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     悬浮窗「刷新」按钮：往系统输入流里打一次 F10，等于玩家在游戏里按了一下 F10，
///     让 3DMigoto / XXMI 重载 Mod。
///
///     不需要找窗口、也不需要游戏在前台 —— 3DMigoto 的热键是每帧用 GetAsyncKeyState
///     轮询的，模拟按键进的是全局输入流，游戏在后台也收得到。
///
///     唯一的坑是 UIPI：游戏以管理员身份运行、而 OMniX 没提权时，模拟按键会被系统
///     静默丢掉 —— 这正是「点了没反应」最常见的原因。所以先查一次提权状态，
///     命中就把原因明明白白告诉用户，而不是悄悄失败。
/// </summary>
internal static class GameKeySender
{
    private const uint VkF10 = 0x79;
    private const ushort F10ScanCode = 0x44;

    private const uint KeyEventFScanCode = 0x0008;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint InputKeyboard = 1;

    /// <summary>按下到抬起之间留足一帧以上的时间，保证 3Dmigoto 每帧轮询能看到跳变。</summary>
    private const int KeyDownHoldMs = 80;

    /// <summary>F10 发送结果，悬浮窗按它决定给什么提示、要不要亮出「管理员重启」按钮。</summary>
    public enum F10SendResult
    {
        Sent,
        GameNotRunning,
        BlockedByUac,
        Failed
    }

    /// <summary>
    ///     发送一次 F10。返回结果分类和给状态栏看的说明文字。
    /// </summary>
    public static (F10SendResult Result, string Message, string Detail)
        SendF10ToGame(IReadOnlyList<string> gameProcessNames)
    {
        var game = FindGameProcess(gameProcessNames);
        if (game is null)
        {
            return (F10SendResult.GameNotRunning, "没找到正在运行的游戏进程，先启动游戏再刷新", string.Empty);
        }

        if (IsProcessElevated(game) && !IsProcessElevatedUsingCurrentProcessToken())
        {
            return (F10SendResult.BlockedByUac,
                "游戏正以管理员身份运行，模拟按键进不去 —— 点右边「以管理员重启」后再刷新", string.Empty);
        }

        var diagnostics = new List<string>
        {
            $"游戏进程={game.ProcessName}(pid {game.Id})",
            $"游戏提权={IsProcessElevated(game)}",
            $"OMniX提权={IsProcessElevatedUsingCurrentProcessToken()}"
        };

        var gameWindow = game.MainWindowHandle;
        if (gameWindow == IntPtr.Zero)
        {
            gameWindow = FindVisibleWindowByPid(game.Id);
            diagnostics.Add("主窗口句柄为空，按 PID 兜底找窗口");
        }

        diagnostics.Add($"游戏窗口=0x{gameWindow.ToInt64():X}");

        // 3Dmigoto 只在游戏窗口是前台时才轮询热键（DispatchInputEvents 前的
        // CheckForegroundWindow）：点按钮时前台是悬浮窗，必须先把焦点切回游戏。
        // 玩家刚点过悬浮窗，OMniX 此刻就是前台进程，SetForegroundWindow 是被允许的。
        var previousWindow = GetForegroundWindow();
        diagnostics.Add($"切换前前台窗口=0x{previousWindow.ToInt64():X}");

        var switched = false;
        if (gameWindow != IntPtr.Zero && gameWindow != previousWindow)
        {
            // 前置：AllowSetForegroundWindow 偶尔会失败，失败也要继续发，别卡在这儿
            switched = SetForegroundWindow(gameWindow);
            diagnostics.Add($"切换焦点到游戏={(switched ? "成功" : "失败")}");

            // 给窗口管理器一点时间真正完成切换
            Thread.Sleep(80);
            diagnostics.Add($"切换后前台窗口=0x{GetForegroundWindow().ToInt64():X}");
        }
        else
        {
            diagnostics.Add("游戏窗口已经是前台，无需切换");
        }

        var injected = 0;
        try
        {
            injected += SendKey(keyUp: false);
            // 按下和抬起必须隔开一帧以上，否则 3Dmigoto 两次轮询都看到抬起，
            // 检测不到 down→up 的跳变
            Thread.Sleep(KeyDownHoldMs);
            injected += SendKey(keyUp: true);
            Thread.Sleep(50);
        }
        finally
        {
            // 焦点还给悬浮窗，别把玩家晾在游戏里
            if (switched)
            {
                SetForegroundWindow(previousWindow);
            }
        }

        diagnostics.Add($"注入的按键事件数={injected}（期望 2）");

        return injected == 2
            ? (F10SendResult.Sent, "已发送 F10，3DMigoto 正在重载 Mod…", string.Join(" | ", diagnostics))
            : (F10SendResult.Failed, "F10 发送失败（系统拒绝了模拟输入）", string.Join(" | ", diagnostics));
    }

    private static Process? FindGameProcess(IReadOnlyList<string> gameProcessNames)
    {
        Process? fallback = null;

        foreach (var name in gameProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                // 优先挑有主窗口的（游戏本体），没有窗口的先记着兜底
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process;
                }

                fallback ??= process;
            }
        }

        return fallback;
    }

    /// <summary>发一次 F10 的按下或抬起，返回成功注入的事件数（0 或 1）。</summary>
    private static int SendKey(bool keyUp)
    {
        // 用扫描码方式发（和真实键盘一样），GetAsyncKeyState / 底层键盘钩子都认
        Span<INPUT> inputs =
        [
            new INPUT
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = F10ScanCode,
                        dwFlags = keyUp ? KeyEventFScanCode | KeyEventFKeyUp : KeyEventFScanCode
                    }
                }
            }
        ];

        return (int)SendInput((uint)inputs.Length, ref MemoryMarshal.GetReference(inputs),
            Marshal.SizeOf<INPUT>());
    }

    /// <summary>进程没有主窗口句柄时，按 PID 找一个可见的顶层窗口兜底。</summary>
    private static IntPtr FindVisibleWindowByPid(int processId)
    {
        var found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    #region Win32

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        out uint tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>按进程句柄查提权状态。查不了就当没提权（宁可多发一次也别误报）。</summary>
    private static bool IsProcessElevated(Process process)
    {
        var handle = IntPtr.Zero;
        var token = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero || !OpenProcessToken(handle, TokenQuery, out token))
            {
                return false;
            }

            return GetTokenInformation(token, TokenElevation, out var elevation, sizeof(uint), out _) &&
                   elevation != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    /// <summary>当前进程（OMniX 自己）有没有提权。</summary>
    private static bool IsProcessElevatedUsingCurrentProcessToken()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
