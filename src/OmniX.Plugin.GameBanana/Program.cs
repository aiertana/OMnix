using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Automation;

namespace OmniX.Plugin.GameBanana;

/// <summary>
///     GameBanana 独立插件的启动器。
///     功能专区的卡片由 OMniX 扫描 plugin.json 生成，点「浏览」拉起本进程。
///     激活主程序里的商店窗口分三级降级，尽量不要求宿主是特定版本：
///     1. 命名管道 —— 新宿主在跑时最准；
///     2. --open-gamebanana 参数拉起宿主 —— 新宿主启动时自会识别；
///     3. UIA 自动化 —— 旧宿主（如 1.4.5）两者都不认识，就等主窗口
///        出现后模拟点「功能专区 → 打开商店/浏览」，跟人手点一样。
/// </summary>
internal static class Program
{
    private const string PipeName = "OMniX.Plugin.GameBananaStore";

    // 兼容新旧宿主的商店入口文案（1.4.5=「打开商店」，新版=「浏览」）
    private static readonly string[] StoreButtonNames = ["打开商店", "浏览"];

    [STAThread]
    private static void Main()
    {
        if (TryNotifyRunningHost())
        {
            return;
        }

        LaunchHostWithFlag();

        // 旧宿主认不了参数，但启动总归没错；等窗口出来后 UIA 兜底
        OpenStoreViaUiAutomation();
    }

    /// <summary>新宿主在跑：发一条 open-gamebanana 就走，不用 UIA。</summary>
    private static bool TryNotifyRunningHost()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.Write("open-gamebanana");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从插件目录逐级向上找宿主 exe。优先启动器壳（用户习惯双击那个），其次 Core 目录里的本体。</summary>
    private static void LaunchHostWithFlag()
    {
        if (FindRunningHost() is not null)
        {
            return; // 已在跑，别再拉一个
        }

        var host = FindHostExe();
        if (host is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(host)
            {
                Arguments = "--open-gamebanana",
                UseShellExecute = true,
                // 日志等相对路径跟着 exe 走，别写进插件目录
                WorkingDirectory = Path.GetDirectoryName(host)
            });
        }
        catch
        {
            // 启动失败就交给 UIA 兜底流程自己报
        }
    }

    private static string? FindHostExe()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && directory is not null; i++)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "Core", "OMniX.exe"),
                         Path.Combine(directory.FullName, "OMniX.exe"),
                         Path.Combine(directory.FullName, "启动OMniX.exe")
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static Process? FindRunningHost()
    {
        return Process.GetProcessesByName("OMniX")
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
    }

    private static void OpenStoreViaUiAutomation()
    {
        try
        {
            var root = WaitForMainWindow(TimeSpan.FromSeconds(40));
            if (root is null)
            {
                return;
            }

            // 侧栏「功能专区」：可能是标题也可能是导航项，挑能点的那个
            var entry = WaitFor(() => FindClickable(root, "功能专区"), TimeSpan.FromSeconds(15));
            if (entry is null)
            {
                return;
            }

            Invoke(entry);

            // 商店卡片的入口按钮，新旧宿主文案都试
            var button = WaitFor(
                () => StoreButtonNames.Select(n => FindClickable(root, n)).FirstOrDefault(b => b is not null),
                TimeSpan.FromSeconds(15));
            if (button is not null)
            {
                Invoke(button);
            }
        }
        catch
        {
            // UIA 失败就算了，至少宿主已经起来了
        }
    }

    private static AutomationElement? WaitForMainWindow(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var proc = FindRunningHost();
            if (proc is not null)
            {
                try
                {
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        return AutomationElement.FromHandle(proc.MainWindowHandle);
                    }
                }
                catch
                {
                    // 窗口还没就绪，继续等
                }
            }

            Thread.Sleep(500);
        }

        return null;
    }

    private static AutomationElement? WaitFor(Func<AutomationElement?> probe, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var found = probe();
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(400);
        }

        return null;
    }

    /// <summary>按名字找「能触发」的元素（按钮/导航项），标题之类的纯文本直接跳过。</summary>
    private static AutomationElement? FindClickable(AutomationElement root, string name)
    {
        var matches = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
        foreach (AutomationElement el in matches)
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var _)
                || el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var _))
            {
                return el;
            }
        }

        return null;
    }

    private static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
        }
    }
}
