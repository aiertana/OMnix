using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace OmniX.Plugin.OilFX;

/// <summary>
///     OilFX 独立插件启动器（GamesData\Plugins\OilFX\OmniX.Plugin.OilFX.exe）：
///     1. 宿主在跑且内置 OilFX 界面 → 命名管道发 "open-oilfx"，宿主管理窗口秒开；
///     2. 宿主在跑但没有 OilFX 界面（旧版宿主）→ 启动插件自带的初始化工具兜底；
///     3. 宿主没跑 → 带 --open-oilfx 拉起宿主，等管道出现（新宿主自动开窗）；
///        超时还没出现（宿主过旧不认参数）→ 同样落到初始化工具兜底。
/// </summary>
internal static class Program
{
    private const string PipeName = "OMniX.Plugin.OilFX";
    private const string LaunchFlag = "--open-oilfx";

    /// <summary>独立版管理界面（与宿主 OilFXWindow 同源 XAML，随插件分发，任何 OMniX 版本都能跑）。</summary>
    private const string StandaloneUi = @"OilFXUI\OmniX.Plugin.OilFX.UI.exe";

    /// <summary>旧兜底：精简版 WinForms 工具（独立版缺失时才用）。</summary>
    private const string FallbackTool = "OilFXTool.exe";

    [STAThread]
    private static void Main()
    {
        // 1. 宿主在跑且支持 OilFX：管道发消息，宿主秒开管理窗口
        if (TryNotifyRunningHost())
        {
            return;
        }

        // 2. 宿主在跑但管道不通 → 旧版宿主，没有内置 OilFX 界面 → 兜底工具
        if (IsHostRunning())
        {
            RunFallbackTool();
            return;
        }

        // 3. 宿主没跑：带 flag 拉起；新宿主初始化后管道出现并自动开窗
        LaunchHostWithFlag();
        if (WaitForPipe(TimeSpan.FromSeconds(40)))
        {
            return;
        }

        // 宿主起来了但不认 --open-oilfx（过旧）→ 兜底工具
        RunFallbackTool();
    }

    private static bool TryNotifyRunningHost()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2500);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write("open-oilfx");
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>轮询等宿主的 OilFX 管道出现（宿主启动 + 初始化需要一点时间）。</summary>
    private static bool WaitForPipe(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client, Encoding.UTF8);
                writer.Write("open-oilfx");
                writer.Flush();
                return true;
            }
            catch
            {
                Thread.Sleep(600);
            }
        }

        return false;
    }

    /// <summary>宿主进程是否正在运行（OMniX.exe）。</summary>
    private static bool IsHostRunning()
    {
        try
        {
            return Process.GetProcessesByName("OMniX").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动插件自带的管理界面（优先独立 WinUI 版，与宿主界面同源；缺失时退回精简版工具）。</summary>
    private static void RunFallbackTool()
    {
        var tool = Path.Combine(AppContext.BaseDirectory, StandaloneUi);
        if (!File.Exists(tool))
        {
            tool = Path.Combine(AppContext.BaseDirectory, FallbackTool);
        }

        if (!File.Exists(tool))
        {
            MessageBox.Show(
                "当前 OMniX 版本不内置 OilFX 管理界面，且插件目录缺少管理界面文件。\n请更新 OMniX 或重新导入完整的 OilFX 插件包。",
                "OilFX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                WorkingDirectory = Path.GetDirectoryName(tool)!,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            MessageBox.Show($"启动管理界面失败：{e.Message}", "OilFX",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void LaunchHostWithFlag()
    {
        var host = FindHostExe();
        if (host is null)
        {
            // 没有 OMniX 宿主 → 直接用插件自带工具独立干活
            RunFallbackTool();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = host,
                Arguments = LaunchFlag,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(host)!
            });
        }
        catch (Exception e)
        {
            MessageBox.Show($"拉起 OMniX 失败：{e.Message}", "OilFX",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>候选：插件目录向上最多 6 级里的 Core\OMniX.exe / OMniX.exe / 启动OMniX.exe。</summary>
    private static string? FindHostExe()
    {
        var dir = AppContext.BaseDirectory;
        var candidates = new[] { "Core\\OMniX.exe", "OMniX.exe", "启动OMniX.exe", "OMniX\\Core\\OMniX.exe" };

        for (var i = 0; i < 6 && dir is not null; i++)
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
